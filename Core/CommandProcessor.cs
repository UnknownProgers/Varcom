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
	private readonly Queue<QueuedInvocation> _executionQueue = new(initialCapacity);
	private readonly List<ParsedCommand> _parsedCommands = new(initialCapacity);
	private readonly ArgumentBuffer _argumentBuffer = new();
	private readonly Dictionary<Type, ICommandBuffer> _commandBuffers = new();
	private bool _updating;

	/// <summary>Parses a command sequence and queues it only if every invocation is valid.</summary>
	public CommandResult Enqueue(string commandLine)
	{
		try
		{
			var result = CommandLineParser.Parse(commandLine, _registry, _parsedCommands, _argumentBuffer);
			if (!result.IsSuccess)
				return result;

			foreach (var command in _parsedCommands)
				_executionQueue.Enqueue(QueuedInvocation.FromParsed(command));

			return CommandResult.Ok();
		}
		finally
		{
			_parsedCommands.Clear();
		}
	}

	/// <summary>Queues typed data. Success means accepted, not executed.</summary>
	public CommandResult Enqueue<TCommand>(in TCommand command) where TCommand : struct, ICommand
	{
		if (!_registry.TryGet<TCommand>(out var binding))
			return CommandResult.Fail(new CommandError(CommandErrorCode.Unavailable, "No handler for command type."));

		var buffer = GetOrCreateBuffer<TCommand>();
		buffer.Add(in command);
		_executionQueue.Enqueue(QueuedInvocation.FromTyped(binding, buffer));
		return CommandResult.Ok();
	}

	/// <summary>
	/// Processes the batch present at entry, optionally delivering each result inline.
	/// No result collection is allocated. A callback exception propagates and stops the batch.
	/// </summary>
	public void Update(Action<CommandResult>? onCompleted = null)
	{
		if (_updating)
			throw new InvalidOperationException("Reentrant Update is not supported.");

		_updating = true;
		try
		{
			var count = _executionQueue.Count;
			for (var i = 0; i < count; i++)
			{
				var result = Execute(_executionQueue.Dequeue());
				onCompleted?.Invoke(result);
			}
		}
		finally
		{
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

	private static CommandResult Execute(QueuedInvocation invocation)
	{
		try
		{
			return invocation.Execute();
		}
		catch (Exception ex)
		{
			return CommandResult.Fail(ex.Message);
		}
	}
}
