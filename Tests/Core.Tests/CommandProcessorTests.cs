using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandProcessorTests
{
	[Fact]
	public void ConstructorValidatesCapacityAndTextConstructionWaitsForUpdate()
	{
		var registry = new CommandRegistry();
		Assert.ThrowsAny<Exception>(() => new CommandProcessor(registry, -1));
		var constructions = 0;
		using var registration = registry.Register(Info(), args =>
		{
			constructions++;
			if (args.Get<int>(0) < 0) throw new InvalidOperationException("Constructor failure");
			return new Add(args.Get<int>(0));
		}, new Handler(_ => CommandResult.Ok()));
		var processor = new CommandProcessor(registry, 0);
		Assert.True(processor.Enqueue("add -1; add 2").IsSuccess);
		Assert.Equal(0, constructions);
		var results = Collect(processor);
		Assert.Equal(CommandErrorCode.ExceptionThrown, results[0].Error.Code);
		Assert.True(results[1].IsSuccess);
		Assert.Equal(2, constructions);
	}

	[Fact]
	public void Enqueue_DoesNotExecuteUntilUpdate()
	{
		var registry = new CommandRegistry();
		var calls = 0;
		using var binding = registry.Register(Info(), a => new Add(a.Get<int>(0), a.Get<int>(1)),
			new Handler(c =>
			{
				calls += c.Value * c.Count;
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);
		var submission = processor.Enqueue(new Add(3, 2));
		Assert.True(submission.IsSuccess);
		Assert.Equal(0, calls);
		var done = Collect(processor);
		Assert.Equal(6, calls);
		Assert.Single(done);
		Assert.True(done[0].IsSuccess);
		Assert.Empty(Collect(processor));
	}

	[Fact]
	public void Update_ProcessesInitialBatchAndContinuesAfterHandlerFailure()
	{
		var registry = new CommandRegistry();
		var log = new List<int>();
		var processor = new CommandProcessor(registry);
		using var binding = registry.Register(Info(), a => new Add(a.Get<int>(0), a.Get<int>(1)),
			new Handler(c =>
			{
				log.Add(c.Value);
				if (c.Value == 1) processor.Enqueue(new Add(3));
				if (c.Value == 2) throw new InvalidOperationException("broken");
				return CommandResult.Ok();
			}));
		processor.Enqueue(new Add(1));
		processor.Enqueue(new Add(2));
		processor.Enqueue(new Add(4));
		var done = Collect(processor);
		Assert.Equal(new[] { 1, 2, 4 }, log);
		Assert.False(done[1].IsSuccess);
		processor.Update();
		Assert.Equal(new[] { 1, 2, 4, 3 }, log);
	}

	[Fact]
	public void Update_ReturnsBusinessFailure()
	{
		var registry = new CommandRegistry();
		using var registration =
			registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(_ => CommandResult.Fail("Inventory full")));
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(1));
		Assert.Equal("Inventory full", Collect(processor)[0].Error.Message);
	}

	[Fact]
	public void Update_RejectsReentrantCalls()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		using var registration = registry.RegisterMethod(new("nested", "", Array.Empty<CommandParameter>()), _ =>
		{
			processor.Update();
			return CommandResult.Ok();
		});
		processor.Enqueue("nested");
		Assert.False(Collect(processor)[0].IsSuccess);
		Assert.Empty(Collect(processor));
	}

	[Fact]
	public void Update_DeliversResultsInOrder()
	{
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)),
			new Handler(c => c.Value < 0 ? CommandResult.Fail("Negative value") : CommandResult.Ok(c.Value)));
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue(new Add(1)).IsSuccess);
		Assert.True(processor.Enqueue(new Add(-1)).IsSuccess);
		var results = Collect(processor);
		Assert.Equal(2, results.Count);
		Assert.True(results[0].IsSuccess);
		Assert.Equal(1, results[0].Value.Get<int>());
		Assert.False(results[1].IsSuccess);
		Assert.Equal("Negative value", results[1].Error.Message);
	}

	[Fact]
	public void Update_PreservesRemainingCallsWhenCallbackThrows()
	{
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(1));
		processor.Enqueue(new Add(2));
		Assert.Throws<InvalidOperationException>(() => processor.Update(_ => throw new InvalidOperationException("Output failed")));
		var remaining = Collect(processor);
		Assert.Single(remaining);
		Assert.Equal(2, remaining[0].Value.Get<int>());
	}

	[Fact]
	public void Processors_DoNotSharePendingCommands()
	{
		var registry = new CommandRegistry();
		var seen = new List<int>();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)),
			new Handler(c =>
			{
				seen.Add(c.Value);
				return CommandResult.Ok();
			}));
		var first = new CommandProcessor(registry);
		var second = new CommandProcessor(registry);
		first.Enqueue(new Add(1));
		second.Enqueue(new Add(2));
		second.Update();
		first.Update();
		Assert.Equal(new[] { 2, 1 }, seen);
	}
}
