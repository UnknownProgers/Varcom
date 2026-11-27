#nullable enable

using System.Collections.Generic;

namespace Varcom.Contracts;

public sealed record CommandInfo(string Name, string Description, IReadOnlyList<CommandParameter> Parameters);
