using System;

namespace Varcom.Contracts;

public sealed record CommandParameter(string Name, Type ValueType, bool IsOptional = false, object? DefaultValue = null);
