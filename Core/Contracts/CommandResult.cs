using System;
using System.Runtime.CompilerServices;

namespace Varcom.Contracts;

/// <summary>A value result. For Enqueue, success means accepted, not executed.</summary>
public readonly record struct CommandResult
{
	public bool IsSuccess => Error.Code == CommandErrorCode.None;

	public CommandError Error { get; }
	public CommandValue Value { get; }

	private CommandResult(CommandError error, CommandValue value)
	{
		Error = error;
		Value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Ok(CommandValue value = default) => new(default, value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(CommandError error)
	{
		if (error.Code == CommandErrorCode.None)
			throw new ArgumentException("Failure needs an error code.", nameof(error));

		return new CommandResult(error, default);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(CommandErrorCode code, string message) => Fail(new CommandError(code, message));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(string message) => Fail(new CommandError(CommandErrorCode.ExecutionFailed, message));
}
