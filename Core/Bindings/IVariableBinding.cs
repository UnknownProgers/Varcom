#nullable enable

using Varcom.Contracts;

namespace Varcom.Bindings;

internal interface IVariableBinding
{
	VariableInfo VariableInfo { get; }
	CommandError ReadOnlyError { get; }
	CommandResult Read();
}
