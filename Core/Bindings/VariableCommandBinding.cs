#nullable enable

using System;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class VariableCommandBinding<T>(
	CommandInfo info,
	VariableInfo variableInfo,
	Func<T> getter,
	Func<T, CommandResult>? setter) : CommandBinding(info), IVariableBinding where T : struct
{
	private Func<T>? _getter = getter;
	private Func<T, CommandResult>? _setter = setter;

	public VariableInfo VariableInfo { get; } = variableInfo;
	public CommandError ReadOnlyError { get; } = new(CommandErrorCode.ReadOnly, $"Variable '{variableInfo.Name}' is read-only.");

	public CommandResult Read() => IsActive ? CommandResult.Ok(CommandValue.From(_getter!())) : Unavailable();

	protected override CommandResult ExecuteCore(CommandArguments arguments)
	{
		return arguments.Count == 0
			? Read()
			: _setter!(arguments.Get<T>(0));
	}

	protected override void Release()
	{
		_getter = null;
		_setter = null;
	}
}
