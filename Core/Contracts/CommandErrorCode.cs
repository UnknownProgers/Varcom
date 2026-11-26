namespace Varcom.Contracts;

public enum CommandErrorCode
{
	None,
	InvalidSyntax,
	UnknownCommand,
	InvalidArguments,
	Unavailable,
	ReadOnly,
	ExecutionFailed
}
