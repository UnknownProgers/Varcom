#nullable enable

using Varcom.Contracts;

namespace Varcom.Parsing;

// Returns immutable token views without lists, strings or a character builder.
internal struct CommandScanner(string text, int start = 0, int end = -1, bool separateLines = false)
{
	private readonly string _text = text;
	private readonly int _end = end < 0 ? text.Length : end;
	internal int Position { get; private set; } = start;

	internal TokenKind Read(out CmdString token)
	{
		token = default;
		while (Position < _end && char.IsWhiteSpace(_text[Position])
		       && !(separateLines && _text[Position] is '\r' or '\n'))
			Position++;

		if (Position == _end)
			return TokenKind.End;

		if (_text[Position] == ';' || (separateLines && _text[Position] is '\r' or '\n'))
		{
			Position++;
			return TokenKind.Separator;
		}

		var start = Position;
		var length = 0;
		var quoted = false;
		var encoded = false;
		while (Position < _end)
		{
			var c = _text[Position];
			if (separateLines && c is '\r' or '\n')
				break;
			if (c == '\\' && quoted && Position + 1 < _end && _text[Position + 1] is '\\' or '"')
			{
				Position += 2;
				length++;
			}
			else if (c == '"')
			{
				quoted = !quoted;
				encoded = true;
				Position++;
			}
			else if (!quoted && (c == ';' || char.IsWhiteSpace(c)))
				break;
			else
			{
				Position++;
				length++;
			}
		}

		if (quoted)
			return TokenKind.Invalid;

		token = new CmdString(_text, start, Position, length, encoded);
		return TokenKind.Token;
	}
}
internal enum TokenKind
{
	Token,
	Separator,
	End,
	Invalid
}
