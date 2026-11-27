using System.Buffers;
using Varcom.Bindings;
using Varcom.Contracts;
using Varcom.Parsing;

namespace Varcom.Processing;

// Text calls own tokens, never registrations; typed calls reference processor-owned payload storage.
internal readonly struct QueuedInvocation
{
	private readonly CommandInvocation _text;
	private readonly ICommandBuffer? _typedBuffer;
	private readonly bool _rentedTokens;

	private QueuedInvocation(CommandInvocation text, ICommandBuffer? typedBuffer, bool rentedTokens = false)
	{
		_text = text;
		_typedBuffer = typedBuffer;
		_rentedTokens = rentedTokens;
	}

	internal static QueuedInvocation FromText(CommandInvocation text, bool rentedTokens = false) => new(text, null, rentedTokens);
	internal static QueuedInvocation FromTyped(ICommandBuffer buffer) => new(default, buffer);

	internal CommandResult Execute(CommandRegistry registry, ArgumentBuffer buffer)
	{
		if (_typedBuffer != null)
			return _typedBuffer.ExecuteNext(registry);

		if (!_text.IsValid)
			return CommandResult.Fail(CommandErrorCode.InvalidSyntax, "Empty command.");

		if (!registry.TryGet(buffer.Decode(_text.Name), out var binding))
			return CommandResult.Fail(CommandErrorCode.UnknownCommand, "Unknown command.");

		var count = _text.ArgumentCount;
		if (binding is IVariableBinding variable && variable.VariableInfo.IsReadOnly && count != 0)
			return CommandResult.Fail(variable.ReadOnlyError);

		CommandError argumentError = default;
		for (var i = 0; i < count && i < binding.Arguments.Count; i++)
			if (!binding.Arguments.TryParse(i, _text.GetArgument(i), buffer, out _))
			{
				argumentError = binding.Arguments.InvalidArgument(i);
				break;
			}

		var error = binding.Arguments.CompleteValidation(count, argumentError);
		return error.Code != CommandErrorCode.None
			? CommandResult.Fail(error)
			: binding.Execute(new CommandArguments(_text, binding.Arguments, buffer));
	}

	internal void Release()
	{
		if (_rentedTokens)
			ArrayPool<CmdString>.Shared.Return(_text.Tokens, clearArray: true);
	}
}
