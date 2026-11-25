using Varcom.Bindings;

namespace Varcom.Parsing;

// Data produced by text parsing, not a scheduled invocation.
internal readonly record struct ParsedCommand(CommandBinding Binding, object? Payload);
