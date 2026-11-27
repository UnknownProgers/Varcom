#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace Varcom.Parsing;

// Called only for enum types, whose underlying storage is always 1, 2, 4 or 8 bytes.
internal static class EnumBits<T> where T : struct
{
	internal static readonly bool IsSigned = typeof(T).IsEnum && Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) is
		TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64;

	internal static ulong To(T value) => Unsafe.SizeOf<T>() switch
	{
		1 => IsSigned ? unchecked((ulong)Unsafe.As<T, sbyte>(ref value)) : Unsafe.As<T, byte>(ref value),
		2 => IsSigned ? unchecked((ulong)Unsafe.As<T, short>(ref value)) : Unsafe.As<T, ushort>(ref value),
		4 => IsSigned ? unchecked((ulong)Unsafe.As<T, int>(ref value)) : Unsafe.As<T, uint>(ref value),
		_ => Unsafe.As<T, ulong>(ref value)
	};

	internal static T From(ulong bits)
	{
		switch (Unsafe.SizeOf<T>())
		{
			case 1:
				var b = (byte)bits;
				return Unsafe.As<byte, T>(ref b);
			case 2:
				var s = (ushort)bits;
				return Unsafe.As<ushort, T>(ref s);
			case 4:
				var i = (uint)bits;
				return Unsafe.As<uint, T>(ref i);
			default:
				return Unsafe.As<ulong, T>(ref bits);
		}
	}
}
