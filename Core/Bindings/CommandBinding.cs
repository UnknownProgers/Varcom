#nullable enable

using Varcom.Contracts;

namespace Varcom.Bindings;

internal abstract class CommandBinding(CommandInfo info)
{
	internal CommandInfo Info { get; } = info;
	internal bool IsActive { get; private set; } = true;

	internal ArgumentParser Arguments { get; } = new(info.Parameters);

	internal CommandResult Execute(CommandArguments arguments) => IsActive ? ExecuteCore(arguments) : Unavailable();

	internal void Deactivate()
	{
		if (!IsActive)
			return;

		IsActive = false;
		Release();
	}

	protected abstract CommandResult ExecuteCore(CommandArguments arguments);
	protected abstract void Release();

	internal static CommandResult Unavailable() => CommandResult.Fail(new CommandError(
		CommandErrorCode.Unavailable, "The command registration has been disposed."));
}
