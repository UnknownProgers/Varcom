#nullable enable

using System;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal sealed class MethodCommandBinding(CommandInfo info, Func<CommandArguments, CommandResult> invoke) : CommandBinding(info)
{
	private Func<CommandArguments, CommandResult>? _invoke = invoke;

	protected override CommandResult ExecuteCore(CommandArguments arguments) => _invoke!(arguments);

	protected override void Release() => _invoke = null;
}
