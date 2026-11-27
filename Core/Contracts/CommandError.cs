#nullable enable

using System;

namespace Varcom.Contracts;

public readonly record struct CommandError
{
	private readonly string? _message;
	public CommandErrorCode Code { get; }
	public string Message => _message ?? "";
	public string? ArgumentName { get; }

	public CommandError(CommandErrorCode code, string message, string? argumentName = null)
	{
		ArgumentNullException.ThrowIfNull(message);
		Code = code;
		_message = message;
		ArgumentName = argumentName;
	}
}
