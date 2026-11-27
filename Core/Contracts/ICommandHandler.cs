#nullable enable

namespace Varcom.Contracts;

public interface ICommandHandler<TCommand> where TCommand : struct, ICommand
{
	CommandResult Handle(in TCommand command);
}
