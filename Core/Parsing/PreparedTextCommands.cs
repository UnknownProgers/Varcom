#nullable enable

using System;
using Varcom.Contracts;
using Varcom.Processing;

namespace Varcom.Parsing;

// Synchronous lease: disposing returns only calls whose ownership was not transferred.
internal readonly struct PreparedTextCommands(CommandTextPreparer owner, CommandResult result) : IDisposable
{
	internal CommandResult Result { get; } = result;
	internal bool TryDequeue(out QueuedInvocation command) => owner.TryDequeue(out command);
	public void Dispose() => owner.ReleaseRemaining();
}
