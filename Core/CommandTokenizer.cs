#nullable enable

using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Varcom.Parsing;

namespace Varcom;

/// <summary>Reads independent calls without consulting a registry. Syntax errors recover at the next physical line.</summary>
public sealed class CommandTokenizer
{
	private CommandScanner _scanner;
	private readonly List<CmdString> _tokens = new();

	/// <param name="text">Immutable source retained by the resulting token views.</param>
	/// <param name="separateLines">Whether physical newlines separate calls, even inside malformed quotes.</param>
	public CommandTokenizer(string text, bool separateLines = true)
	{
		ArgumentNullException.ThrowIfNull(text);
		_scanner = new CommandScanner(text, separateLines: separateLines);
	}

	/// <summary>Returns false at end. A true return may carry a syntax failure; keep reading to continue.</summary>
	public bool TryRead(out CommandInvocation invocation, out CommandResult result)
	{
		invocation = default;
		if (!ReadNext(ref _scanner, _tokens, out result))
			return false;

		if (result.IsSuccess)
			invocation = new CommandInvocation(_tokens.ToArray(), _tokens.Count);

		return true;
	}

	internal static bool ReadNext(ref CommandScanner scanner, List<CmdString> tokens, out CommandResult result)
	{
		tokens.Clear();
		var invalid = false;
		while (true)
		{
			var kind = scanner.Read(out var token);
			switch (kind)
			{
				case TokenKind.Token:
					tokens.Add(token);
					break;
				case TokenKind.Invalid:
					invalid = true;
					break;
				case TokenKind.Separator or TokenKind.End:
				{
					result = invalid
						? CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Unterminated quoted string.")
						: CommandResult.Ok();

					if (invalid || tokens.Count > 0)
						return true;

					if (kind == TokenKind.End)
						return false;

					break;
				}
			}
		}
	}
}
