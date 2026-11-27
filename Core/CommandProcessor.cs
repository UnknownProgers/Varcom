#nullable enable

using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Varcom.Parsing;
using Varcom.Processing;

namespace Varcom;

/// <summary>A synchronous processor of independently waiting FIFO sequences. All operations belong to the host's game thread.</summary>
public sealed class CommandProcessor(CommandRegistry registry, int initialCapacity = 128)
{
	private readonly CommandRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
	private readonly ExecutionQueue _executionQueue = new(initialCapacity);
	private readonly CommandTextPreparer _textPreparer = new(initialCapacity);
	private readonly ArgumentBuffer _argumentBuffer = new();
	private readonly Dictionary<Type, ICommandBuffer> _commandBuffers = new();

	private bool _updating;
	private bool _stopSequence;
	private CommandSequence? _currentSequence;
	private ulong _frame;
	private ulong _suspensionGeneration;

	/// <summary>Extends the current sequence, or creates an independent sequence before pending work outside Update.</summary>
	public void Insert(CommandInvocation command)
	{
		_executionQueue.Insert(_currentSequence ?? _executionQueue.CreateSequence(insert: true), QueuedInvocation.FromText(command));
	}

	/// <summary>Inserts a batch in input order. The span is consumed synchronously and is never retained.</summary>
	public void Insert(ReadOnlySpan<CommandInvocation> commands)
	{
		if (commands.IsEmpty)
			return;

		var sequence = _currentSequence ?? _executionQueue.CreateSequence(insert: true);
		foreach (var command in commands)
			_executionQueue.Insert(sequence, QueuedInvocation.FromText(command));
	}

	/// <summary>Convenience text insertion. Only syntax is checked now; lookup and arguments are checked during Update.</summary>
	public CommandResult Insert(string commandText)
	{
		using var prepared = _textPreparer.Prepare(commandText);
		if (!prepared.Result.IsSuccess)
			return prepared.Result;

		var sequence = _currentSequence ?? _executionQueue.CreateSequence(insert: true);
		while (prepared.TryDequeue(out var command))
			_executionQueue.Insert(sequence, command);

		return CommandResult.Ok();
	}

	/// <summary>Tokenizes a sequence atomically. Success means accepted, not that commands are available or arguments valid.</summary>
	public CommandResult Enqueue(string commandLine)
	{
		using var prepared = _textPreparer.Prepare(commandLine);
		if (!prepared.Result.IsSuccess)
			return prepared.Result;

		var sequence = _executionQueue.CreateSequence();
		while (prepared.TryDequeue(out var command))
			_executionQueue.Enqueue(sequence, command);

		return CommandResult.Ok();
	}

	/// <summary>Creates a one-call sequence for late resolution.</summary>
	public void Enqueue(CommandInvocation command)
	{
		_executionQueue.Enqueue(_executionQueue.CreateSequence(), QueuedInvocation.FromText(command));
	}

	/// <summary>Creates one independent sequence of tokenized calls in input order.</summary>
	public void Enqueue(ReadOnlySpan<CommandInvocation> commands)
	{
		if (commands.IsEmpty)
			return;

		var sequence = _executionQueue.CreateSequence();
		foreach (var command in commands)
			_executionQueue.Enqueue(sequence, QueuedInvocation.FromText(command));
	}

	/// <summary>Creates a one-call typed sequence. Success means accepted, not executed.</summary>
	public CommandResult Enqueue<TCommand>(in TCommand command) where TCommand : struct, ICommand
	{
		var buffer = GetOrCreateBuffer<TCommand>();
		buffer.Add(in command);
		_executionQueue.Enqueue(_executionQueue.CreateSequence(), QueuedInvocation.FromTyped(buffer));
		return CommandResult.Ok();
	}

	/// <summary>Delays the current sequence until the Nth subsequent Update. Requires an executing command or callback.</summary>
	public CommandResult WaitFrames(long frames)
	{
		if (frames < 0)
			return CommandResult.Fail(CommandErrorCode.InvalidArguments, "Invalid frame delay.");

		var sequence = _currentSequence;
		if (sequence == null)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "No active command sequence.");

		if (sequence.Suspended || sequence.WaitUntilFrame > _frame)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "The sequence is already waiting.");

		if (frames == 0)
			return CommandResult.Ok();

		sequence.WaitUntilFrame = _frame + (ulong)frames;
		_stopSequence = true;

		return CommandResult.Ok();
	}

	/// <summary>Suspends the current sequence, leaving independently submitted sequences runnable.</summary>
	public CommandResult Suspend(out CommandSuspension suspension)
	{
		suspension = default;
		var sequence = _currentSequence;
		if (sequence == null)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "No active command sequence.");

		if (sequence.Suspended || sequence.WaitUntilFrame > _frame)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "The sequence is already waiting.");

		_suspensionGeneration = unchecked(_suspensionGeneration + 1);
		sequence.Generation = _suspensionGeneration;
		sequence.Suspended = true;
		_stopSequence = true;

		suspension = new CommandSuspension(this, sequence, sequence.Generation);
		return CommandResult.Ok();
	}

	/// <summary>Signals one sequence. Its continuation runs on a subsequent Update, never inline.</summary>
	public bool Resume(CommandSuspension suspension)
	{
		var sequence = suspension.Sequence;
		if (suspension.Owner != this
		    || sequence == null
		    || !sequence.Suspended
		    || sequence.Generation != suspension.Generation)
			return false;

		sequence.Suspended = false;
		sequence.ResumeFrame = _frame + 1;
		return true;
	}

	/// <summary>
	/// Advances one frame and executes ready sequences in submission order. Handler Enqueue calls wait for the next Update.
	/// Callback exceptions preserve pending work and waits. Insert extends the current sequence.
	/// </summary>
	public void Update(Action<CommandResult>? onCompleted = null)
	{
		if (_updating)
			throw new InvalidOperationException("Reentrant Update is not supported.");

		_frame++;
		var boundary = _executionQueue.Last;
		var sequence = _executionQueue.First;
		_executionQueue.ResetInsertion();
		_updating = true;

		try
		{
			while (sequence != null)
			{
				var last = sequence == boundary;
				var next = sequence.Next;
				if (!sequence.Suspended && sequence.ResumeFrame <= _frame && sequence.WaitUntilFrame <= _frame)
				{
					_currentSequence = sequence;
					_stopSequence = false;
					while (sequence.Head >= 0 && !_stopSequence)
					{
						var invocation = _executionQueue.Dequeue(sequence);
						sequence.InsertionTail = -1;
						var result = Execute(invocation);
						onCompleted?.Invoke(result);
					}

					EndSequence();
				}

				if (last)
					break;

				sequence = next;
			}
		}
		finally
		{
			EndSequence();
			_executionQueue.ResetInsertion();
			_updating = false;
		}
	}

	private void EndSequence()
	{
		var sequence = _currentSequence;
		_currentSequence = null;
		if (sequence != null && sequence.Head < 0)
			_executionQueue.Remove(sequence);
	}

	private TypedCommandBuffer<TCommand> GetOrCreateBuffer<TCommand>() where TCommand : struct, ICommand
	{
		var type = typeof(TCommand);
		if (!_commandBuffers.TryGetValue(type, out var buffer))
		{
			buffer = new TypedCommandBuffer<TCommand>(initialCapacity);
			_commandBuffers.Add(type, buffer);
		}

		return (TypedCommandBuffer<TCommand>)buffer;
	}

	private CommandResult Execute(QueuedInvocation invocation)
	{
		try
		{
			return invocation.Execute(_registry, _argumentBuffer);
		}
		catch (Exception ex)
		{
			return CommandResult.Fail(ex.Message);
		}
		finally
		{
			invocation.Release();
		}
	}
}
