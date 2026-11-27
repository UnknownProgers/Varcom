#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Varcom.Contracts;

namespace Varcom.Parsing;

internal abstract class ValueParser
{
	internal abstract bool TryParse(CmdString token, ArgumentBuffer buffer, out CommandValue value);
	internal abstract CommandValue Default(object? value);

	internal static ValueParser Create(Type type)
	{
		if (type == typeof(int)) return new ValueParser<int>();
		if (type == typeof(bool)) return new ValueParser<bool>();
		if (type == typeof(float)) return new ValueParser<float>();
		if (type == typeof(double)) return new ValueParser<double>();
		if (type == typeof(CmdString)) return new ValueParser<CmdString>();
		// Registration validates supported types. The remaining case is a manually declared enum.
		return (ValueParser)Activator.CreateInstance(typeof(ValueParser<>).MakeGenericType(type), true)!;
	}
}
internal sealed class ValueParser<T> : ValueParser where T : struct
{
	private static readonly T[] EnumValues = typeof(T).IsEnum ? (T[])Enum.GetValues(typeof(T)) : Array.Empty<T>();
	private static readonly string[] EnumNames = typeof(T).IsEnum ? Enum.GetNames(typeof(T)) : Array.Empty<string>();

	internal override CommandValue Default(object? value) => CommandValue.From(value is T typed ? typed : default);

	internal static CommandValue EnumValue(T value)
	{
		for (var i = 0; i < EnumValues.Length; i++)
			if (EqualityComparer<T>.Default.Equals(value, EnumValues[i]))
				return CommandValue.FromEnum(value, EnumNames[i]);
		return CommandValue.FromEnum(value, default);
	}

	internal override bool TryParse(CmdString token, ArgumentBuffer buffer, out CommandValue value)
	{
		value = default;
		if (typeof(T) == typeof(CmdString))
		{
			value = token;
			return true;
		}

		var text = buffer.Decode(token);
		if (typeof(T) == typeof(bool))
		{
			if (text.SequenceEqual("1") || text.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				value = true;
				return true;
			}

			if (text.SequenceEqual("0") || text.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				value = false;
				return true;
			}
		}
		else if (typeof(T) == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
		{
			value = i;
			return true;
		}
		else if (typeof(T) == typeof(float)
		         && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
		         && float.IsFinite(f))
		{
			value = f;
			return true;
		}
		else if (typeof(T) == typeof(double)
		         && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
		         && double.IsFinite(d))
		{
			value = d;
			return true;
		}
		else if (typeof(T).IsEnum && Enum.TryParse<T>(text, true, out var e))
		{
			for (var index = 0; index < EnumValues.Length; index++)
			{
				if (!EqualityComparer<T>.Default.Equals(e, EnumValues[index])) continue;
				value = CommandValue.FromEnum(e, EnumNames[index]);
				return true;
			}
		}

		return false;
	}
}
