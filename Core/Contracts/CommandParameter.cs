#nullable enable

using System;
using System.ComponentModel;
using Varcom.Parsing;

namespace Varcom.Contracts;

public sealed record CommandParameter(string Name, Type ValueType, bool IsOptional = false, object? DefaultValue = null)
{
	internal ValueParser? Parser { get; init; }

	[EditorBrowsable(EditorBrowsableState.Never)]
	public static CommandParameter Create<T>(string name, bool isOptional = false, object? defaultValue = null) where T : struct =>
		new(name, typeof(T), isOptional, defaultValue) { Parser = new ValueParser<T>() };
}
