using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom.Parsing;

// Data produced by text parsing, not a scheduled invocation.
internal readonly record struct ParsedCommand(CommandBinding Binding, CommandArguments Arguments);
