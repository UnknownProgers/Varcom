using System;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class LifetimeTests
{
	[Fact]
	public void Dispose_ReleasesVariableOwnerWhileCallIsPending()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var owner = RegisterAndDispose(registry, processor);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(owner.IsAlive);
		Assert.Equal(CommandErrorCode.Unavailable, Collect(processor)[0].Error?.Code);
	}

	[Fact]
	public void Dispose_ReleasesTypedHandlerWhileCallIsPending()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var handler = RegisterTypedAndDispose(registry, processor);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(handler.IsAlive);
		var result = Collect(processor)[0];
		Assert.False(result.IsSuccess);
		Assert.Equal(CommandErrorCode.Unavailable, result.Error.Code);
	}
}
