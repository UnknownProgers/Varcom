namespace Varcom.Contracts;

public enum CommandErrorCode
{
	InvalidSyntax,
	UnknownCommand,
	InvalidArguments,
	Unavailable,
	ReadOnly,
	ExecutionFailed
}
