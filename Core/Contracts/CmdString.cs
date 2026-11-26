using System;

namespace Varcom.Contracts;

/// <summary>Immutable text view. Retains its source string and remains valid after Update.</summary>
public readonly struct CmdString : IEquatable<CmdString>
{
	private readonly string? _source;
	private readonly int _start;
	private readonly int _end;
	private readonly bool _encoded;
	public int Length { get; }

	public CmdString(string? text)
	{
		_source = text;
		_start = 0;
		_end = text?.Length ?? 0;
		Length = _end;
		_encoded = false;
	}

	internal CmdString(string source, int start, int end, int length, bool encoded)
	{
		_source = source;
		_start = start;
		_end = end;
		Length = length;
		_encoded = encoded;
	}

	internal bool TryGetSpan(out ReadOnlySpan<char> text)
	{
		text = _source.AsSpan(_start, _end - _start);
		return !_encoded;
	}

	public bool TryCopyTo(Span<char> destination)
	{
		if (destination.Length < Length)
			return false;

		if (!_encoded)
		{
			_source.AsSpan(_start, _end - _start).CopyTo(destination);
			return true;
		}

		var written = 0;
		foreach (var c in this)
			destination[written++] = c;

		return true;
	}

	public bool Equals(ReadOnlySpan<char> text)
	{
		if (Length != text.Length)
			return false;

		if (TryGetSpan(out var span))
			return span.SequenceEqual(text);

		var index = 0;
		foreach (var c in this)
			if (c != text[index++])
				return false;

		return true;
	}

	public bool Equals(string? text) => text != null && Equals(text.AsSpan());

	public bool Equals(CmdString other)
	{
		if (Length != other.Length)
			return false;

		if (TryGetSpan(out var leftSpan) && other.TryGetSpan(out var rightSpan))
			return leftSpan.SequenceEqual(rightSpan);

		var right = other.GetEnumerator();
		foreach (var c in this)
		{
			right.MoveNext();
			if (c != right.Current)
				return false;
		}

		return true;
	}

	public override bool Equals(object? obj) => obj is CmdString text && Equals(text);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var c in this)
			hash.Add(c);

		return hash.ToHashCode();
	}

	public static bool operator ==(CmdString left, CmdString right) => left.Equals(right);
	public static bool operator !=(CmdString left, CmdString right) => !left.Equals(right);

	public Enumerator GetEnumerator() => new(this);

	/// <summary>Explicitly materializes decoded text. May allocate.</summary>
	public override string ToString() => !_encoded
		? _source?.Substring(_start, _end - _start) ?? ""
		: string.Create(Length, this, static (span, text) => text.TryCopyTo(span));

	public static implicit operator CmdString(string? text) => new(text);

	public struct Enumerator
	{
		private readonly CmdString _text;
		private int _position;
		private bool _quoted;
		public char Current { get; private set; }

		internal Enumerator(CmdString text)
		{
			_text = text;
			_position = text._start;
			_quoted = false;
			Current = default;
		}

		public bool MoveNext()
		{
			while (_position < _text._end)
			{
				var c = _text._source![_position++];
				if (_text._encoded)
				{
					if (c == '"')
					{
						_quoted = !_quoted;
						continue;
					}

					if (c == '\\' && _quoted && _position < _text._end && _text._source[_position] is '\\' or '"')
						c = _text._source[_position++];
				}

				Current = c;
				return true;
			}

			return false;
		}
	}
}
