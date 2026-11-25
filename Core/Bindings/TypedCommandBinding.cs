using System;
using System.Collections.Generic;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class TypedCommandBinding<TCommand>(
	CommandInfo info,
	Func<IReadOnlyList<object?>, TCommand> factory,
	ICommandHandler<TCommand> handler) : CommandBinding(info)
	where TCommand : struct, ICommand
{
	private Func<IReadOnlyList<object?>, TCommand>? _factory = factory;
	private ICommandHandler<TCommand>? _handler = handler;

	internal override object CreatePayload(IReadOnlyList<object?> arguments) => _factory!(arguments);

	internal CommandResult Handle(in TCommand command) => IsActive ? _handler!.Handle(in command) : Unavailable();

	protected override CommandResult ExecuteCore(object? payload)
	{
		var command = (TCommand)payload!;
		return Handle(in command);
	}

	protected override void Release()
	{
		_factory = null;
		_handler = null;
	}
}
