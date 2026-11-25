using System;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

[Collection("Allocation tests")]
[Trait("Category", "Allocations")]
public sealed class AllocationTests
{
	[Fact]
	public void Update_EmptyProcessorDoesNotAllocate()
	{
		var processor = new CommandProcessor(new CommandRegistry());
		for (var i = 0; i < 1000; i++)
		{
			processor.Update();
			_ = CommandResult.Ok();
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 1000; i++)
		{
			processor.Update();
			_ = CommandResult.Ok();
		}

		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
	}

	[Fact]
	public void EnqueueAndUpdate_TypedCommandsDoNotAllocateAfterWarmup()
	{
		var registry = new CommandRegistry();
		var count = 0;
		using var registration = registry.Register(Info(), a => new Add((int)a[0]!),
			new Handler(c =>
			{
				count += c.Value;
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);
		for (var i = 0; i < 1000; i++)
		{
			processor.Enqueue(new Add(1));
			processor.Update();
		}

		const int iterations = 10000;
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++)
		{
			processor.Enqueue(new Add(1));
			processor.Update();
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(iterations + 1000, count);

		Assert.Equal(0L, allocated);
	}

	[Fact]
	public void EnqueueAndUpdate_MixedStructBatchesAndCachedCallbackDoNotAllocate()
	{
		var registry = new CommandRegistry();
		var sum = 0;
		var wideHandler = new WideHandler();
		using var first = registry.Register(Info(), a => new Add((int)a[0]!),
			new Handler(c =>
			{
				sum += c.Value;
				return CommandResult.Ok();
			}));
		using var second = registry.Register(new CommandInfo("wide", "", Array.Empty<CommandParameter>()),
			_ => new WideCommand(1, 2, 3, 4, 5, 6, 7, 8, "stable"), wideHandler);
		var processor = new CommandProcessor(registry);
		Action<CommandResult> onResult = result =>
		{
			if (!result.IsSuccess) throw new Exception(result.Error.Message);
		};
		const int batchSize = 16;
		for (var iteration = 0; iteration < 100; iteration++)
		{
			for (var i = 0; i < batchSize; i++)
			{
				processor.Enqueue(new Add(1));
				processor.Enqueue(new WideCommand(1, 2, 3, 4, 5, 6, 7, 8, "stable"));
			}

			processor.Update(onResult);
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var iteration = 0; iteration < 1000; iteration++)
		{
			for (var i = 0; i < batchSize; i++)
			{
				processor.Enqueue(new Add(1));
				processor.Enqueue(new WideCommand(1, 2, 3, 4, 5, 6, 7, 8, "stable"));
			}

			processor.Update(onResult);
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(1100 * batchSize, sum);
		Assert.Equal(sum, wideHandler.Count);

		Assert.Equal(0L, allocated);
	}
}
