using Varcom.Contracts;

namespace Varcom.Processing;

internal interface ICommandBuffer
{
	CommandResult ExecuteNext(CommandRegistry registry);
}
