using System;
using System.ComponentModel;
using Varcom.Parsing;

namespace Varcom.Contracts;

/// <summary>Validated text arguments used by generated bindings. Values are read without boxing.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public struct CommandArguments
{
	private readonly string _source;
	private readonly int _start;
	private readonly int _end;
	private readonly ArgumentParser _parser;
	private readonly ArgumentBuffer _buffer;
	private int _position;
	private int _nextIndex;
	public int Count { get; }

	internal CommandArguments(string source,
		int start,
		int end,
		int count,
		ArgumentParser parser,
		ArgumentBuffer buffer)
	{
		_source = source;
		_start = start;
		_end = end;
		Count = count;
		_parser = parser;
		_buffer = buffer;
		_position = start;
		_nextIndex = 0;
	}

	public T Get<T>(int index) where T : struct
	{
		if ((uint)index >= (uint)_parser.Count)
			throw new ArgumentOutOfRangeException(nameof(index));

		if (index >= Count)
			return _parser.Default(index).Get<T>();

		if (index < _nextIndex)
		{
			_position = _start;
			_nextIndex = 0;
		}

		var scanner = new CommandScanner(_source, _position, _end);
		CmdString token = default;
		while (_nextIndex <= index)
		{
			if (scanner.Read(out token) != TokenKind.Token)
				throw new InvalidOperationException("Invalid argument view.");

			_nextIndex++;
		}

		_position = scanner.Position;
		if (!_parser.TryParse(index, token, _buffer, out var value))
			throw new InvalidOperationException("Invalid argument value.");

		return value.Get<T>();
	}
}
