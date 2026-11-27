namespace Varcom.Contracts;

/// <summary>Identifies one external suspension of one processor. Default and stale values cannot resume it.</summary>
public readonly struct CommandSuspension
{
	internal CommandProcessor? Owner { get; }
	internal ulong Generation { get; }

	internal CommandSuspension(CommandProcessor owner, ulong generation)
	{
		Owner = owner;
		Generation = generation;
	}
}
