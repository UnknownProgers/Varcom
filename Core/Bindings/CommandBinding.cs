using System.Collections.Generic;
using Varcom.Contracts;

namespace Varcom.Bindings;

internal abstract class CommandBinding(CommandInfo info)
{
	internal CommandInfo Info { get; } = info;
	internal bool IsActive { get; private set; } = true;

	internal abstract object? CreatePayload(IReadOnlyList<object?> arguments);

	internal CommandResult Execute(object? payload) => IsActive ? ExecuteCore(payload) : Unavailable();

	internal void Deactivate()
	{
		if (!IsActive)
			return;

		IsActive = false;
		Release();
	}

	protected abstract CommandResult ExecuteCore(object? payload);
	protected abstract void Release();

	internal static CommandResult Unavailable() => CommandResult.Fail(new CommandError(
		CommandErrorCode.Unavailable, "The command registration has been disposed."));
}
