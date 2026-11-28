#nullable enable

using System;

namespace Varcom.Contracts;

public readonly record struct CommandError
{
	private readonly string? _message;
	public CommandErrorCode Code { get; }
	public string Message => _message ?? "";
	public string? ArgumentName { get; }

	/// <summary>The original exception when Code is ExceptionThrown; null for ordinary failures.</summary>
	public Exception? Exception { get; }

	public CommandError(CommandErrorCode code, string message, string? argumentName = null)
	{
		ArgumentNullException.ThrowIfNull(message);
		Code = code;
		_message = message;
		ArgumentName = argumentName;
		Exception = null;
	}

	public CommandError(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		Code = CommandErrorCode.ExceptionThrown;
		_message = exception.Message;
		ArgumentName = null;
		Exception = exception;
	}
}
