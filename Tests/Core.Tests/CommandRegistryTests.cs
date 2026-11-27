using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandRegistryTests
{
	[Fact]
	public void MetadataSnapshotsAreCachedUntilRegistrationChanges()
	{
		var registry = new CommandRegistry();
		using var first = registry.RegisterMethod(new("first", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok());
		var original = registry.GetEntries();
		Assert.Same(original, registry.GetEntries());
		var second = registry.RegisterMethod(new("second", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok());
		var expanded = registry.GetEntries();
		Assert.NotSame(original, expanded);
		Assert.Single(original);
		Assert.Equal(2, expanded.Count);
		second.Dispose();
		Assert.Single(registry.GetEntries());
		Assert.Equal(2, expanded.Count);
	}

	[Fact]
	public void PendingTypedCallsUseReplacement()
	{
		var registry = new CommandRegistry();
		var calls = 0;
		var processor = new CommandProcessor(registry);
		var old = registry.Register(Info(), a => new Add(a.Get<int>(0), a.Get<int>(1)),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		processor.Enqueue(new Add(1));
		old.Dispose();
		old.Dispose();
		using var replacement = registry.Register(Info(), a => new Add(a.Get<int>(0), a.Get<int>(1)),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		Assert.True(Collect(processor)[0].IsSuccess);
		Assert.Equal(1, calls);
		Assert.Throws<ArgumentException>(() => registry.Register(Info(), a => new Add(), new Handler(_ => CommandResult.Ok())));
		processor.Enqueue(new Add(1));
		processor.Update();
		Assert.Equal(2, calls);
	}

	[Fact]
	public void RegisterGroup_RollsBackOnDuplicateWithoutRemovingExistingCommand()
	{
		var registry = new CommandRegistry();
		using var existing = registry.RegisterMethod(new("taken", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok());
		Assert.Throws<ArgumentException>(() => RegistrationGroup.Create(
			() => registry.RegisterMethod(new("new", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok()),
			() => registry.RegisterMethod(new("taken", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok())));
		Assert.Single(registry.GetEntries());
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("taken").IsSuccess);
		Assert.True(processor.Enqueue("new").IsSuccess);
		Assert.Equal(CommandErrorCode.UnknownCommand, Collect(processor)[1].Error.Code);
	}

	[Fact]
	public void Register_CopiesCallerOwnedMetadata()
	{
		var registry = new CommandRegistry();
		var parameters = new[] { new CommandParameter("value", typeof(int)) };
		using var registration = registry.RegisterMethod(new("test", "", parameters), _ => CommandResult.Ok());
		parameters[0] = new("value", typeof(CmdString));
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("test invalid").IsSuccess);
		Assert.Equal(CommandErrorCode.InvalidArguments, Collect(processor)[0].Error.Code);
	}

	[Fact]
	public void Update_PreservesMixedInvocationOrderAfterReplacement()
	{
		var registry = new CommandRegistry();
		var seen = new List<int>();
		var handler = new Handler(c =>
		{
			seen.Add(c.Value);
			return CommandResult.Ok();
		});
		var old = registry.Register(Info(), a => new Add(a.Get<int>(0)), handler);
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(99));
		old.Dispose();
		using var current = registry.Register(Info(), a => new Add(a.Get<int>(0)), handler);
		processor.Enqueue("add 1");
		processor.Enqueue(new Add(2));
		processor.Enqueue("add 3");
		var results = Collect(processor);
		Assert.True(results[0].IsSuccess);
		Assert.Equal(new[] { 99, 1, 2, 3 }, seen);
	}

	[Fact]
	public void Update_DoesNotRedirectFactoryResultToReplacement()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var calls = 0;
		var handler = new Handler(_ =>
		{
			calls++;
			return CommandResult.Ok();
		});
		IDisposable? original = null;
		IDisposable? replacement = null;
		original = registry.Register(Info(), args =>
		{
			original!.Dispose();
			replacement = registry.Register(Info(), values => new Add(values.Get<int>(0)), handler);
			return new Add(args.Get<int>(0));
		}, handler);
		try
		{
			var result = processor.Enqueue("add 1");
			Assert.True(result.IsSuccess);
			Assert.Equal(0, calls);
			Assert.Equal(CommandErrorCode.Unavailable, Collect(processor)[0].Error.Code);
			Assert.Equal(0, calls);
			Assert.True(processor.Enqueue(new Add(2)).IsSuccess);
			processor.Update();
			Assert.Equal(1, calls);
		}
		finally
		{
			original.Dispose();
			replacement?.Dispose();
		}
	}
}
