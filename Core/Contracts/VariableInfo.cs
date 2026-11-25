using System;

namespace Varcom.Contracts;

public sealed record VariableInfo(string Name, string Description, Type ValueType, bool IsReadOnly);
