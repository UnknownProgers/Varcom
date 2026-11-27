#nullable enable

using System;

namespace Varcom;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CmdHandlerAttribute(Type commandType) : Attribute
{
	public Type CommandType { get; } = commandType;
}
