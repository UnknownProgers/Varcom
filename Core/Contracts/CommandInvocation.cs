using System;

namespace Varcom.Contracts;

/// <summary>An immutable tokenized text call, independent of any active registration.</summary>
public readonly struct CommandInvocation
{
	private readonly CmdString[]? _tokens;
	internal int TokenCount { get; }
	internal CmdString[] Tokens => _tokens!;
	public bool IsValid => TokenCount > 0;
	public CmdString Name => IsValid ? _tokens![0] : default;
	public int ArgumentCount => IsValid ? TokenCount - 1 : 0;

	internal CommandInvocation(CmdString[] tokens, int count)
	{
		_tokens = tokens;
		TokenCount = count;
	}

	public CmdString GetArgument(int index)
	{
		if ((uint)index >= (uint)ArgumentCount)
			throw new ArgumentOutOfRangeException(nameof(index));

		return _tokens![index + 1];
	}
}
