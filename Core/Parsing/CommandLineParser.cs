using System;
using System.Collections.Generic;
using Varcom.Bindings;
using Varcom.Contracts;

namespace Varcom.Parsing;

internal static class CommandLineParser
{
	internal static CommandResult Parse(string commandLine, CommandRegistry registry, out ParsedCommand[] commands)
	{
		commands = Array.Empty<ParsedCommand>();
		var statements = CommandText.Tokenize(commandLine, out var error);
		if (error != null)
			return CommandResult.Fail(error);

		if (statements.Count == 0)
			return CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Empty command.");

		var parsed = new ParsedCommand[statements.Count];
		for (var i = 0; i < statements.Count; i++)
		{
			var result = ParseCommand(statements[i], registry, out parsed[i]);
			if (!result.IsSuccess)
				return result;
		}

		commands = parsed;
		return CommandResult.Ok();
	}

	private static CommandResult ParseCommand(IReadOnlyList<string> tokens, CommandRegistry registry, out ParsedCommand command)
	{
		command = default;
		if (!registry.TryGet(tokens[0], out var binding))
			return CommandResult.Fail(CommandErrorCode.UnknownCommand, $"Unknown command '{tokens[0]}'.");

		var isVariable = binding is IVariableBinding;
		var argumentCount = tokens.Count - 1;
		if (binding is IVariableBinding { VariableInfo.IsReadOnly: true } && argumentCount != 0)
			return CommandResult.Fail(CommandErrorCode.ReadOnly, $"Variable '{tokens[0]}' is read-only.");

		var error = ArgumentParser.Parse(tokens, binding.Info.Parameters, out var values, 1);
		if (error != null)
			return CommandResult.Fail(error);

		try
		{
			var arguments = isVariable && argumentCount == 0 ? Array.Empty<object?>() : values;
			var payload = binding.CreatePayload(arguments);
			if (!binding.IsActive)
				return CommandBinding.Unavailable();

			command = new ParsedCommand(binding, payload);
			return CommandResult.Ok();
		}
		catch (Exception ex)
		{
			return CommandResult.Fail(CommandErrorCode.InvalidArguments, ex.Message);
		}
	}
}
