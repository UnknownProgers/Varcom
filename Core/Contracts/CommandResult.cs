using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Varcom.Contracts;

/// <summary>A value result. For Enqueue, success means accepted, not executed.</summary>
public readonly record struct CommandResult
{
	[MemberNotNullWhen(false, nameof(Error))]
	public bool IsSuccess => Error is null;

	public CommandError? Error { get; }
	public object? Value { get; }

	private CommandResult(CommandError? error, object? value)
	{
		Error = error;
		Value = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Ok(object? value = null) => new(null, value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(CommandError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		return new CommandResult(error, null);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(CommandErrorCode code, string message) => Fail(new CommandError(code, message));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static CommandResult Fail(string message) => Fail(new CommandError(CommandErrorCode.ExecutionFailed, message));
}
