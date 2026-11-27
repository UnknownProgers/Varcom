#nullable enable

namespace Varcom.Contracts;

public sealed record CommandEntry(CommandInfo Info, VariableInfo? Variable);
