using System;
using System.ComponentModel;
using Varcom.Parsing;

namespace Varcom.Contracts;

/// <summary>Validated tokenized arguments used by generated bindings. Values are read without boxing.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public struct CommandArguments
{
	private readonly CommandInvocation _invocation;
	private readonly ArgumentParser _parser;
	private readonly ArgumentBuffer _buffer;
	public int Count => _invocation.ArgumentCount;

	internal CommandArguments(CommandInvocation invocation, ArgumentParser parser, ArgumentBuffer buffer)
	{
		_invocation = invocation;
		_parser = parser;
		_buffer = buffer;
	}

	public T Get<T>(int index) where T : struct
	{
		if ((uint)index >= (uint)_parser.Count)
			throw new ArgumentOutOfRangeException(nameof(index));

		if (index >= Count)
			return _parser.Default(index).Get<T>();

		if (!_parser.TryParse(index, _invocation.GetArgument(index), _buffer, out var value))
			throw new InvalidOperationException("Invalid argument value.");

		return value.Get<T>();
	}
}
