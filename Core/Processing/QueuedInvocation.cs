using Varcom.Bindings;
using Varcom.Contracts;
using Varcom.Parsing;

namespace Varcom.Processing;

// Explicit alternatives: typed-buffer data or validated text arguments.
internal readonly struct QueuedInvocation
{
	private readonly CommandBinding _binding;
	private readonly ICommandBuffer? _typedBuffer;
	private readonly CommandArguments _arguments;

	private QueuedInvocation(CommandBinding binding, ICommandBuffer? typedBuffer, CommandArguments arguments)
	{
		_binding = binding;
		_typedBuffer = typedBuffer;
		_arguments = arguments;
	}

	internal static QueuedInvocation FromTyped(CommandBinding binding, ICommandBuffer buffer) => new(binding, buffer, default);

	internal static QueuedInvocation FromParsed(ParsedCommand command) => new(command.Binding, null, command.Arguments);

	internal CommandResult Execute() => _typedBuffer?.ExecuteNext(_binding) ?? _binding.Execute(_arguments);
}
