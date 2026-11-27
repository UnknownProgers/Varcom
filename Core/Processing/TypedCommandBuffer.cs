using System.Collections.Generic;
using Varcom.Contracts;

namespace Varcom.Processing;

// Payload storage belongs to one processor. The execution queue owns the global order.
internal sealed class TypedCommandBuffer<TCommand>(int initialCapacity) : ICommandBuffer where TCommand : struct, ICommand
{
	private readonly Queue<TCommand> _commands = new(initialCapacity);

	internal void Add(in TCommand command) => _commands.Enqueue(command);

	public CommandResult ExecuteNext(CommandRegistry registry)
	{
		// Consume the payload even when no handler is currently available.
		var command = _commands.Dequeue();
		return registry.TryGet<TCommand>(out var binding)
			? binding.Handle(in command)
			: CommandResult.Fail(CommandErrorCode.Unavailable, "No handler for command type.");
	}
}
