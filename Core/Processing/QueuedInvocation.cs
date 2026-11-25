using Varcom.Bindings;
using Varcom.Contracts;
using Varcom.Parsing;

namespace Varcom.Processing;

// Explicit alternatives: typed-buffer data or the payload of a parsed text invocation.
internal readonly struct QueuedInvocation
{
	private readonly CommandBinding _binding;
	private readonly ICommandBuffer? _typedBuffer;
	private readonly object? _parsedPayload;

	private QueuedInvocation(CommandBinding binding, ICommandBuffer? typedBuffer, object? parsedPayload)
	{
		_binding = binding;
		_typedBuffer = typedBuffer;
		_parsedPayload = parsedPayload;
	}

	internal static QueuedInvocation FromTyped(CommandBinding binding, ICommandBuffer buffer) => new(binding, buffer, null);

	internal static QueuedInvocation FromParsed(ParsedCommand command) => new(command.Binding, null, command.Payload);

	internal CommandResult Execute() => _typedBuffer?.ExecuteNext(_binding) ?? _binding.Execute(_parsedPayload);
}
