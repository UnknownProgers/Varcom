using System;
using System.Collections.Generic;
using System.Globalization;
using Varcom.Contracts;

namespace Varcom;

internal static class ArgumentParser
{
	internal static bool Supports(Type type) => type == typeof(string) || type == typeof(bool)
		|| type == typeof(int) || type == typeof(float) ||
		type == typeof(double) || type.IsEnum;

	internal static string Format(object? value) => value is IFormattable formattable
		? formattable.ToString(null, CultureInfo.InvariantCulture) ?? ""
		: value?.ToString() ?? "";

	internal static CommandError? Parse(IReadOnlyList<string> args, IReadOnlyList<CommandParameter> parameters, out object?[] values,
		int startIndex = 0)
	{
		values = parameters.Count == 0 ? Array.Empty<object?>() : new object?[parameters.Count];
		var argumentCount = args.Count - startIndex;
		if (argumentCount > parameters.Count)
			return new CommandError(CommandErrorCode.InvalidArguments, "Too many arguments.");

		for (var i = 0; i < parameters.Count; i++)
		{
			var parameter = parameters[i];
			if (i >= argumentCount)
			{
				if (!parameter.IsOptional)
					return new CommandError(
						CommandErrorCode.InvalidArguments,
						$"Missing argument '{parameter.Name}'.", parameter.Name);

				values[i] = parameter.DefaultValue;
			}
			else if (!TryParse(args[i + startIndex], parameter.ValueType, out values[i]))
				return new CommandError(
					CommandErrorCode.InvalidArguments,
					$"Expected {parameter.ValueType.Name} for '{parameter.Name}', got '{args[i + startIndex]}'.", parameter.Name);
		}

		return null;
	}

	private static bool TryParse(string text, Type type, out object? value)
	{
		value = null;
		if (type == typeof(string))
		{
			value = text;
			return true;
		}

		if (type == typeof(bool))
		{
			if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				value = true;
				return true;
			}

			if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				value = false;
				return true;
			}

			return false;
		}

		if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
		{
			value = integer;
			return true;
		}

		if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) &&
		    float.IsFinite(single))
		{
			value = single;
			return true;
		}

		if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
		    double.IsFinite(number))
		{
			value = number;
			return true;
		}

		if (type.IsEnum && Enum.TryParse(type, text, true, out var enumeration) && enumeration != null && Enum.IsDefined(type, enumeration))
		{
			value = enumeration;
			return true;
		}

		return false;
	}
}
