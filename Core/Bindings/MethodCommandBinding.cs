using System;
using System.Collections.Generic;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class MethodCommandBinding(CommandInfo info, Func<IReadOnlyList<object?>, CommandResult> invoke) : CommandBinding(info)
{
	private Func<IReadOnlyList<object?>, CommandResult>? _invoke = invoke;

	internal override object CreatePayload(IReadOnlyList<object?> arguments) => arguments;

	protected override CommandResult ExecuteCore(object? payload) => _invoke!((IReadOnlyList<object?>)payload!);

	protected override void Release() => _invoke = null;
}
