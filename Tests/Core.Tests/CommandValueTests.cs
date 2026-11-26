using System;
using Varcom.Contracts;
using Xunit;

namespace Varcom.Tests;

public sealed class CommandValueTests
{
	private enum SignedByte : sbyte
	{
		Negative = -1
	}

	private enum UnsignedByte : byte
	{
		Max = byte.MaxValue
	}

	private enum SignedShort : short
	{
		Negative = -1
	}

	private enum UnsignedShort : ushort
	{
		Max = ushort.MaxValue
	}

	private enum SignedInt : int
	{
		Negative = -1
	}

	private enum UnsignedInt : uint
	{
		Max = uint.MaxValue
	}

	private enum SignedLong : long
	{
		Negative = -1
	}

	private enum UnsignedLong : ulong
	{
		Max = ulong.MaxValue
	}

	[Fact]
	public void ValuesPreserveTheirTypesAndEnumUnderlyingBits()
	{
		Check(12345);
		Check(true);
		Check(1.25f);
		Check(2.5d);
		Check(new CmdString("text"));
		Check(SignedByte.Negative);
		Check(UnsignedByte.Max);
		Check(SignedShort.Negative);
		Check(UnsignedShort.Max);
		Check(SignedInt.Negative);
		Check(UnsignedInt.Max);
		Check(SignedLong.Negative);
		Check(UnsignedLong.Max);
		Assert.Throws<InvalidOperationException>(() => CommandValue.From(1).Get<double>());
		Assert.Throws<ArgumentException>(() => CommandValue.From(1m));
	}

	[Fact]
	public void FormattingUsesInvariantValuesWithoutChangingEnumSignedness()
	{
		Assert.Equal("-2", CommandValue.From((SignedByte)(-2)).ToString());
		Assert.Equal("-2", CommandValue.From((SignedShort)(-2)).ToString());
		Assert.Equal("-2", CommandValue.From((SignedInt)(-2)).ToString());
		Assert.Equal("-2", CommandValue.From((SignedLong)(-2)).ToString());
		Assert.Equal("18446744073709551614", CommandValue.From((UnsignedLong)(ulong.MaxValue - 1)).ToString());
		Assert.Equal("Max", CommandValue.From(UnsignedLong.Max).ToString());
		Assert.Equal("1.25", CommandValue.From(1.25f).ToString());
		Span<char> shortBuffer = stackalloc char[1];
		Assert.False(CommandValue.From(12345).TryFormat(shortBuffer, out var written));
		Assert.Equal(0, written);
		Assert.True(default(CommandValue).TryFormat(shortBuffer, out written));
		Assert.Equal(0, written);
	}

	[Fact]
	public void TextSupportsEmptyValuesEqualityAndCallerOwnedBuffers()
	{
		CmdString text = "hello";
		Span<char> buffer = stackalloc char[5];
		Assert.True(text.TryCopyTo(buffer));
		Assert.True(buffer.SequenceEqual("hello"));
		Assert.False(text.TryCopyTo(buffer[..4]));
		Assert.True(default(CmdString).Equals(""));
		Assert.True(text == new CmdString("hello"));
		Assert.Equal(text.GetHashCode(), new CmdString("hello").GetHashCode());
	}

	private static void Check<T>(T expected) where T : struct => Assert.Equal(expected, CommandValue.From(expected).Get<T>());
}
