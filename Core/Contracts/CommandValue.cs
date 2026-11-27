#nullable enable

using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Varcom.Parsing;

namespace Varcom.Contracts;

/// <summary>A supported result or argument value stored without boxing.</summary>
public readonly struct CommandValue : IEquatable<CommandValue>
{
	private readonly long _bits;
	private readonly CmdString _text;
	private readonly Type? _enumType;
	private readonly bool _enumSigned;
	public CommandValueKind Kind { get; }

	private CommandValue(CommandValueKind kind,
		long bits = 0,
		CmdString text = default,
		Type? enumType = null,
		bool enumSigned = false)
	{
		Kind = kind;
		_bits = bits;
		_text = text;
		_enumType = enumType;
		_enumSigned = enumSigned;
	}

	internal static CommandValue FromEnum<T>(T value, CmdString name) where T : struct =>
		new(CommandValueKind.Enum, unchecked((long)EnumBits<T>.To(value)), name, typeof(T), EnumBits<T>.IsSigned);

	public T Get<T>() where T : struct
	{
		if (typeof(T) == typeof(int) && Kind == CommandValueKind.Int32)
		{
			var v = (int)_bits;
			return Unsafe.As<int, T>(ref v);
		}

		if (typeof(T) == typeof(bool) && Kind == CommandValueKind.Boolean)
		{
			var v = _bits != 0;
			return Unsafe.As<bool, T>(ref v);
		}

		if (typeof(T) == typeof(float) && Kind == CommandValueKind.Single)
		{
			var v = BitConverter.Int32BitsToSingle((int)_bits);
			return Unsafe.As<float, T>(ref v);
		}

		if (typeof(T) == typeof(double) && Kind == CommandValueKind.Double)
		{
			var v = BitConverter.Int64BitsToDouble(_bits);
			return Unsafe.As<double, T>(ref v);
		}

		if (typeof(T) == typeof(CmdString) && Kind == CommandValueKind.Text)
		{
			var v = _text;
			return Unsafe.As<CmdString, T>(ref v);
		}

		if (typeof(T).IsEnum && Kind == CommandValueKind.Enum && _enumType == typeof(T))
			return EnumBits<T>.From(unchecked((ulong)_bits));

		throw new InvalidOperationException($"Cannot read {Kind} as {typeof(T).Name}.");
	}

	public static CommandValue From<T>(T value) where T : struct
	{
		if (typeof(T) == typeof(int)) return Unsafe.As<T, int>(ref value);
		if (typeof(T) == typeof(bool)) return Unsafe.As<T, bool>(ref value);
		if (typeof(T) == typeof(float)) return Unsafe.As<T, float>(ref value);
		if (typeof(T) == typeof(double)) return Unsafe.As<T, double>(ref value);
		if (typeof(T) == typeof(CmdString)) return Unsafe.As<T, CmdString>(ref value);
		if (typeof(T).IsEnum) return ValueParser<T>.EnumValue(value);
		throw new ArgumentException($"Unsupported command value {typeof(T).Name}.");
	}

	public bool TryFormat(Span<char> destination, out int written)
	{
		switch (Kind)
		{
			case CommandValueKind.None:
				written = 0;
				return true;
			case CommandValueKind.Text:
			case CommandValueKind.Enum when _text.Length != 0:
				written = _text.Length;
				if (_text.TryCopyTo(destination))
					return true;
				written = 0;
				return false;
			case CommandValueKind.Boolean:
				var text = _bits == 0 ? "False" : "True";
				written = text.Length;
				if (text.AsSpan().TryCopyTo(destination))
					return true;
				written = 0;
				return false;
			case CommandValueKind.Int32:
				return ((int)_bits).TryFormat(destination, out written, provider: CultureInfo.InvariantCulture);
			case CommandValueKind.Single:
				return BitConverter
					.Int32BitsToSingle((int)_bits)
					.TryFormat(destination, out written, provider: CultureInfo.InvariantCulture);
			case CommandValueKind.Double:
				return BitConverter
					.Int64BitsToDouble(_bits)
					.TryFormat(destination, out written, provider: CultureInfo.InvariantCulture);
			case CommandValueKind.Enum:
				return _enumSigned
					? _bits.TryFormat(destination, out written, provider: CultureInfo.InvariantCulture)
					: unchecked((ulong)_bits).TryFormat(destination, out written, provider: CultureInfo.InvariantCulture);
			default:
				written = 0;
				return false;
		}
	}

	public bool Equals(CommandValue other) => Kind == other.Kind && _bits == other._bits &&
		_enumType == other._enumType && (Kind != CommandValueKind.Text || _text.Equals(other._text));

	public override bool Equals(object? obj) => obj is CommandValue value && Equals(value);
	public override int GetHashCode() => HashCode.Combine(Kind, _bits, _enumType, Kind == CommandValueKind.Text ? _text.GetHashCode() : 0);
	public static bool operator ==(CommandValue left, CommandValue right) => left.Equals(right);
	public static bool operator !=(CommandValue left, CommandValue right) => !left.Equals(right);

	/// <summary>Explicitly materializes formatted output. Use TryFormat to avoid allocations.</summary>
	public override string ToString()
	{
		if (Kind == CommandValueKind.Text || Kind == CommandValueKind.Enum && _text.Length != 0)
			return _text.ToString();

		Span<char> buffer = stackalloc char[64];
		return TryFormat(buffer, out var written)
			? new string(buffer[..written])
			: "";
	}

	public static implicit operator CommandValue(int value) => new(CommandValueKind.Int32, value);
	public static implicit operator CommandValue(bool value) => new(CommandValueKind.Boolean, value ? 1 : 0);
	public static implicit operator CommandValue(float value) => new(CommandValueKind.Single, BitConverter.SingleToInt32Bits(value));
	public static implicit operator CommandValue(double value) => new(CommandValueKind.Double, BitConverter.DoubleToInt64Bits(value));
	public static implicit operator CommandValue(CmdString value) => new(CommandValueKind.Text, text: value);
	public static implicit operator CommandValue(string? value) => new(CommandValueKind.Text, text: new CmdString(value));
}
