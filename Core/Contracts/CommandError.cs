namespace Varcom.Contracts;

public sealed record CommandError(CommandErrorCode Code, string Message, string? ArgumentName = null);
