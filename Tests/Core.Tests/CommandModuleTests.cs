using System;
using System.Runtime.CompilerServices;
using Varcom.Contracts;
using Xunit;

namespace Varcom.Tests;

public sealed class CommandModuleTests
{
	[Fact]
	public void ModuleRegistersAllOwnersAndDisposesOnlyRegistrations()
	{
		var registry = new CommandRegistry();
		var handler = new GeneratedHandler();
		var owner = new GeneratedOwner();
		var module = new TestModule(registry, handler, owner);
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("generated item 2; number 5").IsSuccess);
		processor.Update();
		Assert.Equal(2, handler.Count);
		Assert.Equal(5, owner.Number);

		module.Dispose();
		module.Dispose();

		Assert.Empty(registry.GetEntries());
		Assert.False(owner.IsDisposed);
		Assert.Equal(CommandErrorCode.UnknownCommand, processor.Enqueue("number").Error.Code);
	}

	[Fact]
	public void ConstructionRollsBackEarlierOwnersWhenAnotherOwnerConflicts()
	{
		var registry = new CommandRegistry();
		using var existing = registry.RegisterMethod(new("number", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok());
		Assert.Throws<ArgumentException>(() => new TestModule(registry, new GeneratedHandler(), new GeneratedOwner()));
		Assert.Single(registry.GetEntries());
		Assert.Equal("number", registry.GetEntries()[0].Info.Name);
	}

	[Fact]
	public void NullDependenciesDoNotLeaveRegistrationsBehind()
	{
		var registry = new CommandRegistry();
		Assert.Throws<ArgumentNullException>(() => new TestModule(registry, new GeneratedHandler(), null!));
		Assert.Empty(registry.GetEntries());
	}

	[Fact]
	public void QueuedOldCallsAreNotRedirectedToANewModule()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var previous = new TestModule(registry, new GeneratedHandler(), new GeneratedOwner());
		processor.Enqueue("number 7");
		processor.Enqueue(new GeneratedCommand("old", 5));
		previous.Dispose();
		var handler = new GeneratedHandler();
		var owner = new GeneratedOwner();
		using var current = new TestModule(registry, handler, owner);
		var results = TestCommands.Collect(processor);
		Assert.All(results, result => Assert.Equal(CommandErrorCode.Unavailable, result.Error.Code));
		Assert.Equal(0, owner.Number);
		Assert.Equal(0, handler.Count);
		Assert.True(processor.Enqueue("number 9; generated new 2").IsSuccess);
		processor.Update();
		Assert.Equal(9, owner.Number);
		Assert.Equal(2, handler.Count);
	}

	[Fact]
	public void DisposedModuleDoesNotRetainOwnersEvenWithQueuedCalls()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var (module, weak) = RegisterAndDispose(registry, processor);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		Assert.False(weak.IsAlive);
		GC.KeepAlive(module);
		GC.KeepAlive(processor);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (TestModule, WeakReference) RegisterAndDispose(CommandRegistry registry, CommandProcessor processor)
	{
		var owner = new GeneratedOwner();
		var module = new TestModule(registry, new GeneratedHandler(), owner);
		processor.Enqueue("number 1");
		module.Dispose();
		return (module, new WeakReference(owner));
	}
}
