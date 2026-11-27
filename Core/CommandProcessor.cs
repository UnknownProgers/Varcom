#nullable enable

using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Varcom.Parsing;
using Varcom.Processing;

namespace Varcom;

/// <summary>A synchronous FIFO processor. All operations belong to the host's game thread.</summary>
public sealed class CommandProcessor(CommandRegistry registry, int initialCapacity = 128)
{
	private readonly CommandRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
	private readonly ExecutionQueue _executionQueue = new(initialCapacity);
	private readonly CommandTextPreparer _textPreparer = new(initialCapacity);
	private readonly ArgumentBuffer _argumentBuffer = new();
	private readonly Dictionary<Type, ICommandBuffer> _commandBuffers = new();

	private bool _updating;
	private bool _stopBatch;
	private bool _suspended;
	private long _remainingFrames;
	private ulong _suspensionGeneration;

	/// <summary>Inserts an immutable tokenized call before pending work. Registration is resolved at execution.</summary>
	public void Insert(CommandInvocation command)
	{
		_executionQueue.Insert(QueuedInvocation.FromText(command));
	}

	/// <summary>Inserts a batch in input order. The span is consumed synchronously and is never retained.</summary>
	public void Insert(ReadOnlySpan<CommandInvocation> commands)
	{
		foreach (var command in commands)
			_executionQueue.Insert(QueuedInvocation.FromText(command));
	}

	/// <summary>Convenience text insertion. Only syntax is checked now; lookup and arguments are checked during Update.</summary>
	public CommandResult Insert(string commandText)
	{
		using var prepared = _textPreparer.Prepare(commandText);
		if (!prepared.Result.IsSuccess)
			return prepared.Result;

		while (prepared.TryDequeue(out var command))
			_executionQueue.Insert(command);

		return CommandResult.Ok();
	}

	/// <summary>Tokenizes a sequence atomically. Success means accepted, not that commands are available or arguments valid.</summary>
	public CommandResult Enqueue(string commandLine)
	{
		using var prepared = _textPreparer.Prepare(commandLine);
		if (!prepared.Result.IsSuccess)
			return prepared.Result;

		while (prepared.TryDequeue(out var command))
			_executionQueue.Enqueue(command);

		return CommandResult.Ok();
	}

	/// <summary>Queues a tokenized call for late resolution.</summary>
	public void Enqueue(CommandInvocation command)
	{
		_executionQueue.Enqueue(QueuedInvocation.FromText(command));
	}

	/// <summary>Queues tokenized calls in input order.</summary>
	public void Enqueue(ReadOnlySpan<CommandInvocation> commands)
	{
		foreach (var command in commands)
			_executionQueue.Enqueue(QueuedInvocation.FromText(command));
	}

	/// <summary>Queues typed data. Success means accepted, not executed.</summary>
	public CommandResult Enqueue<TCommand>(in TCommand command) where TCommand : struct, ICommand
	{
		var buffer = GetOrCreateBuffer<TCommand>();
		buffer.Add(in command);
		_executionQueue.Enqueue(QueuedInvocation.FromTyped(buffer));
		return CommandResult.Ok();
	}

	/// <summary>Resumes on the Nth subsequent Update. Zero does not pause; frames mean processor updates.</summary>
	public CommandResult WaitFrames(long frames)
	{
		if (frames < 0)
			return CommandResult.Fail(CommandErrorCode.InvalidArguments, "Invalid frame delay.");

		if (_suspended || _remainingFrames > 0)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "The processor is already waiting.");

		if (frames == 0)
			return CommandResult.Ok();

		_remainingFrames = frames;
		_stopBatch = true;

		return CommandResult.Ok();
	}

	/// <summary>Starts one external wait, or returns failure if already waiting. Failed requests output a default token.</summary>
	public CommandResult Suspend(out CommandSuspension suspension)
	{
		suspension = default;
		if (_suspended || _remainingFrames > 0)
			return CommandResult.Fail(CommandErrorCode.ExecutionFailed, "The processor is already waiting.");

		_suspensionGeneration = unchecked(_suspensionGeneration + 1);
		_suspended = true;
		_stopBatch = true;

		suspension = new CommandSuspension(this, _suspensionGeneration);
		return CommandResult.Ok();
	}

	/// <summary>Accepts only the active suspension's signal. Execution continues on a subsequent Update, never inline.</summary>
	public bool Resume(CommandSuspension suspension)
	{
		if (!_suspended || suspension.Owner != this || suspension.Generation != _suspensionGeneration)
			return false;

		_suspended = false;
		return true;
	}

	/// <summary>
	/// Processes the current batch unless paused. Each call advances a frame wait by one frame.
	/// Results are delivered before stopping. Callback exceptions preserve remaining work and waits.
	/// </summary>
	public void Update(Action<CommandResult>? onCompleted = null)
	{
		if (_updating)
			throw new InvalidOperationException("Reentrant Update is not supported.");

		if (_suspended)
			return;

		if (_remainingFrames > 0 && --_remainingFrames > 0)
			return;

		_stopBatch = false;

		var boundary = _executionQueue.BeginBatch();
		_updating = true;
		try
		{
			while (_executionQueue.First != boundary && !_stopBatch)
			{
				var invocation = _executionQueue.Dequeue();
				_executionQueue.ResetInsertion();
				var result = Execute(invocation);
				onCompleted?.Invoke(result);
			}
		}
		finally
		{
			_executionQueue.EndBatch(boundary);
			_executionQueue.ResetInsertion();
			_updating = false;
		}
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
