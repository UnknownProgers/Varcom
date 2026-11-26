using System;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class TypedCommandBinding<TCommand>(
	CommandInfo info,
	Func<CommandArguments, TCommand> factory,
	ICommandHandler<TCommand> handler) : CommandBinding(info)
	where TCommand : struct, ICommand
{
	private Func<CommandArguments, TCommand>? _factory = factory;
	private ICommandHandler<TCommand>? _handler = handler;

	internal CommandResult Handle(in TCommand command) => IsActive ? _handler!.Handle(in command) : Unavailable();

	protected override CommandResult ExecuteCore(CommandArguments arguments)
	{
		var command = _factory!(arguments);
		return Handle(in command);
	}

	protected override void Release()
	{
		_factory = null;
		_handler = null;
	}
}
