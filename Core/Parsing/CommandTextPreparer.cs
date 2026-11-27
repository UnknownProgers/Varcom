#nullable enable

using System.Buffers;
using System.Collections.Generic;
using Varcom.Contracts;
using Varcom.Processing;

namespace Varcom.Parsing;

// Owns preparation buffers and all token rentals until individual calls transfer to the queue.
internal sealed class CommandTextPreparer(int initialCapacity)
{
	private readonly List<CmdString> _tokens = new(initialCapacity);
	private readonly List<QueuedInvocation> _commands = new(initialCapacity);
	private int _position;

	internal PreparedTextCommands Prepare(string text)
	{
		var scanner = new CommandScanner(text, separateLines: true);
		try
		{
			while (CommandTokenizer.ReadNext(ref scanner, _tokens, out var result))
			{
				if (!result.IsSuccess)
					return new PreparedTextCommands(this, result);

				var tokens = ArrayPool<CmdString>.Shared.Rent(_tokens.Count);
				_tokens.CopyTo(tokens);
				_commands.Add(QueuedInvocation.FromText(new CommandInvocation(tokens, _tokens.Count), rentedTokens: true));
			}

			return new PreparedTextCommands(this, _commands.Count == 0
				? CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Empty command.")
				: CommandResult.Ok());
		}
		finally
		{
			_tokens.Clear();
		}
	}

	internal bool TryDequeue(out QueuedInvocation command)
	{
		command = default;
		if (_position == _commands.Count)
			return false;

		command = _commands[_position];
		_commands[_position++] = default;
		return true;
	}

	internal void ReleaseRemaining()
	{
		for (var i = _position; i < _commands.Count; i++)
			_commands[i].Release();

		_commands.Clear();
		_position = 0;
	}
}
