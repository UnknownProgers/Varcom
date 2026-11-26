using System;
using Varcom.Contracts;

namespace Varcom.Parsing;

// Only numeric/name parsing uses this scratch space; no command retains its spans.
internal sealed class ArgumentBuffer
{
	private char[] _characters = new char[128];

	internal ReadOnlySpan<char> Decode(CmdString text)
	{
		if (text.TryGetSpan(out var span))
			return span;

		if (_characters.Length < text.Length)
			Array.Resize(ref _characters, Math.Max(text.Length, _characters.Length * 2));

		text.TryCopyTo(_characters);
		return _characters.AsSpan(0, text.Length);
	}
}
