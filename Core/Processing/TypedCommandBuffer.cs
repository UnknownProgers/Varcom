using System.Collections.Generic;
using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom.Processing;

// Payload storage belongs to one processor. The execution queue owns the global order.
internal sealed class TypedCommandBuffer<TCommand> : ICommandBuffer where TCommand : struct, ICommand
{
	private readonly Queue<TCommand> _commands = new();

	internal void Add(in TCommand command) => _commands.Enqueue(command);

	public CommandResult ExecuteNext(CommandBinding binding)
	{
		// Always consume the payload, including calls invalidated by registration disposal.
		var command = _commands.Dequeue();
		return ((TypedCommandBinding<TCommand>)binding).Handle(in command);
	}
}
