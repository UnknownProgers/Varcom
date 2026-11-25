using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandRegistryTests
{
	[Fact]
	public void Dispose_InvalidatesOldCallsWithoutRemovingReplacement()
	{
		var registry = new CommandRegistry();
		var calls = 0;
		var processor = new CommandProcessor(registry);
		var old = registry.Register(Info(), a => new Add((int)a[0]!, (int)a[1]!),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		processor.Enqueue(new Add(1));
		old.Dispose();
		old.Dispose();
		using var replacement = registry.Register(Info(), a => new Add((int)a[0]!, (int)a[1]!),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		Assert.Equal(CommandErrorCode.Unavailable, Collect(processor)[0].Error?.Code);
		Assert.Equal(0, calls);
		Assert.Throws<ArgumentException>(() => registry.Register(Info(), a => new Add(), new Handler(_ => CommandResult.Ok())));
		processor.Enqueue(new Add(1));
		processor.Update();
		Assert.Equal(1, calls);
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
		Assert.False(processor.Enqueue("new").IsSuccess);
	}

	[Fact]
	public void Register_CopiesCallerOwnedMetadata()
	{
		var registry = new CommandRegistry();
		var parameters = new[] { new CommandParameter("value", typeof(int)) };
		using var registration = registry.RegisterMethod(new("test", "", parameters), _ => CommandResult.Ok());
		parameters[0] = new("value", typeof(string));
		Assert.False(new CommandProcessor(registry).Enqueue("test invalid").IsSuccess);
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
		var old = registry.Register(Info(), a => new Add((int)a[0]!), handler);
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(99));
		old.Dispose();
		using var current = registry.Register(Info(), a => new Add((int)a[0]!), handler);
		processor.Enqueue("add 1");
		processor.Enqueue(new Add(2));
		processor.Enqueue("add 3");
		var results = Collect(processor);
		Assert.False(results[0].IsSuccess);
		Assert.Equal(CommandErrorCode.Unavailable, results[0].Error?.Code);
		Assert.Equal(new[] { 1, 2, 3 }, seen);
	}

	[Fact]
	public void Enqueue_DoesNotRedirectFactoryResultToReplacement()
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
			replacement = registry.Register(Info(), values => new Add((int)values[0]!), handler);
			return new Add((int)args[0]!);
		}, handler);
		try
		{
			var result = processor.Enqueue("add 1");
			Assert.False(result.IsSuccess);
			Assert.Equal(CommandErrorCode.Unavailable, result.Error.Code);
			processor.Update();
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
