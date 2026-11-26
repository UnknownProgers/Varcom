using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Varcom.Parsing;

namespace Varcom;

internal sealed class ArgumentParser
{
	private readonly IReadOnlyList<CommandParameter> _parameters;
	private readonly ValueParser[] _parsers;
	private readonly CommandValue[] _defaults;
	private readonly CommandError[] _missingErrors;
	private readonly CommandError[] _invalidErrors;
	internal int Count => _parsers.Length;

	internal ArgumentParser(IReadOnlyList<CommandParameter> parameters)
	{
		_parameters = parameters;
		_parsers = new ValueParser[parameters.Count];
		_defaults = new CommandValue[parameters.Count];
		_missingErrors = new CommandError[parameters.Count];
		_invalidErrors = new CommandError[parameters.Count];
		for (var i = 0; i < parameters.Count; i++)
		{
			_parsers[i] = parameters[i].Parser ?? ValueParser.Create(parameters[i].ValueType);
			_defaults[i] = _parsers[i].Default(parameters[i].DefaultValue);
			_missingErrors[i] = new(CommandErrorCode.InvalidArguments, $"Missing argument '{parameters[i].Name}'.", parameters[i].Name);
			_invalidErrors[i] = new(CommandErrorCode.InvalidArguments,
				$"Expected {parameters[i].ValueType.Name} for '{parameters[i].Name}'.", parameters[i].Name);
		}
	}

	internal static bool Supports(Type type) => type == typeof(CmdString)
		|| type == typeof(bool)
		|| type == typeof(int)
		|| type == typeof(float)
		|| type == typeof(double)
		|| type.IsEnum;

	internal CommandValue Default(int index) => _defaults[index];

	internal bool TryParse(int index, CmdString token, ArgumentBuffer buffer, out CommandValue value) =>
		_parsers[index].TryParse(token, buffer, out value);

	internal CommandError Validate(string source, int start, int end, int count, ArgumentBuffer buffer)
	{
		if (count > Count) return new(CommandErrorCode.InvalidArguments, "Too many arguments.");
		var scanner = new CommandScanner(source, start, end);
		for (var i = 0; i < Count; i++)
		{
			var parameter = _parameters[i];
			if (i >= count)
			{
				if (!parameter.IsOptional)
					return _missingErrors[i];
				continue;
			}

			scanner.Read(out var token);
			if (!TryParse(i, token, buffer, out _))
				return _invalidErrors[i];
		}

		return default;
	}
}
