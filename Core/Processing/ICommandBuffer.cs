using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom.Processing;

internal interface ICommandBuffer
{
	CommandResult ExecuteNext(CommandBinding binding);
}
