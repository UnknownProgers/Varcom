using System;

namespace Varcom;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class VarAttribute(string name, string description = "") : Attribute
{
	public string Name { get; } = name;
	public string Description { get; } = description;
}
