#nullable enable

using Varcom.Processing;

namespace Varcom.Contracts;

/// <summary>Identifies one external suspension of one command sequence. Default and stale values cannot resume it.</summary>
public readonly struct CommandSuspension
{
	internal CommandProcessor? Owner { get; }
	internal CommandSequence? Sequence { get; }
	internal ulong Generation { get; }

	internal CommandSuspension(CommandProcessor owner, CommandSequence sequence, ulong generation)
	{
		Owner = owner;
		Sequence = sequence;
		Generation = generation;
	}
}
