using System;
using System.Collections.Generic;
using System.Text;
using Varcom.Contracts;

namespace Varcom;

internal static class CommandText
{
	// Tokenize the entire input in one pass; semicolons inside quotes are ordinary text.
	internal static IReadOnlyList<IReadOnlyList<string>> Tokenize(string text, out CommandError? error)
	{
		ArgumentNullException.ThrowIfNull(text);
		var commands = new List<IReadOnlyList<string>>();
		var tokens = new List<string>();
		var buffer = new StringBuilder();
		var quoted = false;
		var started = false;
		error = null;
		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (c == '\\' && quoted && i + 1 < text.Length && (text[i + 1] == '\\' || text[i + 1] == '"'))
			{
				buffer.Append(text[++i]);
				started = true;
			}
			else if (c == '"')
			{
				quoted = !quoted;
				started = true;
			}
			else if (!quoted && c == ';')
			{
				FinishCommand();
			}
			else if (!quoted && char.IsWhiteSpace(c))
			{
				FinishToken();
			}
			else
			{
				buffer.Append(c);
				started = true;
			}
		}

		if (quoted)
		{
			error = new CommandError(CommandErrorCode.InvalidSyntax, "Unterminated quoted string.");
			return Array.Empty<IReadOnlyList<string>>();
		}

		FinishCommand();
		return commands;

		void FinishToken()
		{
			if (!started)
				return;

			tokens.Add(buffer.ToString());
			buffer.Clear();
			started = false;
		}

		void FinishCommand()
		{
			FinishToken();
			if (tokens.Count == 0)
				return;

			commands.Add(tokens);
			tokens = new List<string>();
		}
	}
}
