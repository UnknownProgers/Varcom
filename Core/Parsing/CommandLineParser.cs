using System;
using System.Collections.Generic;
using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom.Parsing;

internal static class CommandLineParser
{
	internal static CommandResult Parse(string text, CommandRegistry registry, List<ParsedCommand> commands, ArgumentBuffer buffer)
	{
		ArgumentNullException.ThrowIfNull(text);
		var scanner = new CommandScanner(text);
		CommandBinding? binding = null;
		var start = 0;
		var count = 0;
		CommandError argumentError = default;

		while (true)
		{
			var kind = scanner.Read(out var token);
			if (kind == TokenKind.Invalid)
				return CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Unterminated quoted string.");

			if (kind == TokenKind.Token)
			{
				if (binding == null)
				{
					if (!registry.TryGet(buffer.Decode(token), out binding))
						return CommandResult.Fail(CommandErrorCode.UnknownCommand, "Unknown command.");
					start = scanner.Position;
				}
				else
				{
					if (count < binding.Arguments.Count && argumentError.Code == CommandErrorCode.None &&
					    !binding.Arguments.TryParse(count, token, buffer, out _))
						argumentError = binding.Arguments.InvalidArgument(count);
					count++;
				}

				continue;
			}

			if (binding != null)
			{
				if (binding is IVariableBinding variable && variable.VariableInfo.IsReadOnly && count != 0)
					return CommandResult.Fail(variable.ReadOnlyError);

				var end = kind == TokenKind.Separator ? scanner.Position - 1 : scanner.Position;
				var error = binding.Arguments.CompleteValidation(count, argumentError);
				if (error.Code != CommandErrorCode.None)
					return CommandResult.Fail(error);

				commands.Add(new ParsedCommand(binding, new CommandArguments(text, start, end, count, binding.Arguments, buffer)));
				binding = null;
				count = 0;
				argumentError = default;
			}

			if (kind == TokenKind.End)
				break;
		}

		return commands.Count == 0 ? CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Empty command.") : CommandResult.Ok();
	}
}
