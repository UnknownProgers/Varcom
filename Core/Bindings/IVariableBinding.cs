using Varcom.Contracts;

namespace Varcom.Bindings;

internal interface IVariableBinding
{
	VariableInfo VariableInfo { get; }
	CommandResult Read();
}
