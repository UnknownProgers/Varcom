using System;

namespace Varcom;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Method, Inherited = false)]
public sealed class CmdAttribute(string name, string description = "") : Attribute
{
	public string Name { get; } = name;
	public string Description { get; } = description;
}
