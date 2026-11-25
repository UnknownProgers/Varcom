using System;
using System.Collections.Generic;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class VariableCommandBinding<T>(
	CommandInfo info,
	VariableInfo variableInfo,
	Func<T> getter,
	Func<T, CommandResult>? setter) : CommandBinding(info), IVariableBinding
{
	private Func<T>? _getter = getter;
	private Func<T, CommandResult>? _setter = setter;

	public VariableInfo VariableInfo { get; } = variableInfo;

	internal override object CreatePayload(IReadOnlyList<object?> arguments) => arguments;

	public CommandResult Read() => IsActive ? CommandResult.Ok(_getter!()) : Unavailable();

	protected override CommandResult ExecuteCore(object? payload)
	{
		var arguments = (IReadOnlyList<object?>)payload!;
		return arguments.Count == 0
			? CommandResult.Ok(ArgumentParser.Format(_getter!()))
			: _setter!((T)arguments[0]!);
	}

	protected override void Release()
	{
		_getter = null;
		_setter = null;
	}
}
