#nullable enable

using System;

namespace Varcom;

/// <summary>Generates a disposable registration module. An empty list selects all local attributed owners.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CmdModuleAttribute(params Type[] ownerTypes) : Attribute
{
	public Type[] OwnerTypes { get; } = ownerTypes;
}
