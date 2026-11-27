using System;
using Varcom.Contracts;
using Varcom.Generated.Assembly_Core_Tests;
using Xunit;

namespace Varcom.Tests;

[Collection("Allocation tests")]
[Trait("Category", "Allocations")]
public sealed class GeneratedAllocationTests
{
	[Fact]
	public void RepeatedMetadataGenerationAndReadingDoNotAllocate()
	{
		var registry = new CommandRegistry();
		registry.GenerateMetadata();
		var entries = registry.GetMetadata();
		for (var i = 0; i < 1000; i++) registry.GenerateMetadata();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 10000; i++)
		{
			registry.GenerateMetadata();
			registry.GetMetadata();
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0L, allocated);
		Assert.Same(entries, registry.GetMetadata());
		Assert.Empty(registry.GetEntries());
	}

	[Theory]
	[InlineData("missing", CommandErrorCode.UnknownCommand)]
	[InlineData("generated ok; missing", CommandErrorCode.UnknownCommand)]
	[InlineData("generated", CommandErrorCode.InvalidArguments)]
	[InlineData("number invalid", CommandErrorCode.InvalidArguments)]
	[InlineData("number 1 2", CommandErrorCode.InvalidArguments)]
	[InlineData("generated \"unfinished", CommandErrorCode.InvalidSyntax)]
	[InlineData(" ; ; ", CommandErrorCode.InvalidSyntax)]
	[InlineData("readonly 2", CommandErrorCode.ReadOnly)]
	public void ExpectedRejectionsDoNotAllocate(string text, CommandErrorCode code)
	{
		var registry = new CommandRegistry();
		using var commands = registry.Register(new GeneratedHandler());
		using var variables = registry.Register(new GeneratedOwner());
		var processor = new CommandProcessor(registry);
		var matches = 0;
		Action<CommandResult> callback = result =>
		{
			if (result.Error.Code == code) matches++;
		};
		for (var i = 0; i < 1000; i++)
		{
			processor.Enqueue(text);
			processor.Update();
		}

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 10000; i++)
		{
			if (processor.Enqueue(text).Error.Code == code) matches++;
			processor.Update(callback);
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(10000, matches);
		Assert.Equal(0L, allocated);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void GeneratedBindings_ParseQueueExecuteReadAndFormatWithoutAllocations(bool generateMetadata)
	{
		var registry = new CommandRegistry();
		if (generateMetadata) registry.GenerateMetadata();
		var handler = new GeneratedHandler();
		var owner = new GeneratedOwner();
		using var commands = new TestModule(registry, handler, owner);
		var processor = new CommandProcessor(registry, initialCapacity: 128);
		var completed = 0;
		var failures = 0;
		Action<CommandResult> callback = result =>
		{
			if (!result.IsSuccess) failures++;
			Span<char> buffer = stackalloc char[64];
			if (!result.Value.TryFormat(buffer, out _)) failures++;
			completed++;
		};
		const string sequence =
			"generated \"hello \\\"world\\\"\"; method \"a;b\" true 1.25 2.5; number 12345; number; label \"x\\\\y\"; label; mode -1; mode; enabled 1; enabled; single 1.5; single; double 2.5; double";
		for (var i = 0; i < 1000; i++)
		{
			processor.Enqueue(sequence);
			processor.Update(callback);
			registry.ReadVariable("number");
			registry.GetEntries();
			registry.IsAvailable("number");
			_ = registry.GetMetadata().Count;
		}

		const int iterations = 10000;
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < iterations; i++)
		{
			processor.Enqueue(sequence);
			processor.Update(callback);
			registry.ReadVariable("number");
			registry.GetEntries();
			registry.IsAvailable("number");
			_ = registry.GetMetadata().Count;
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0, failures);
		Assert.Equal((1000 + iterations) * 14, completed);
		Assert.Equal(1000 + iterations, handler.Count);
		Assert.Equal(1000 + iterations, owner.Calls);
		Assert.True(handler.Text.Equals("hello \"world\""));
		Assert.True(owner.Label.Equals("x\\y"));
		Assert.Equal(GeneratedMode.Negative, owner.Mode);
		Assert.Equal(0L, allocated);
	}

	[Fact]
	public void TextViewRemainsValidAfterQueueGrowthAndSubsequentUpdates()
	{
		var registry = new CommandRegistry();
		var handler = new GeneratedHandler();
		using var commands = registry.Register(handler);
		var processor = new CommandProcessor(registry, initialCapacity: 1);
		Assert.True(processor.Enqueue("generated \"a\\\"b\"").IsSuccess);
		processor.Update();
		var retained = handler.Text;
		for (var i = 0; i < 300; i++) Assert.True(processor.Enqueue("generated next").IsSuccess);
		processor.Update();
		Assert.Equal(301, handler.Count);
		Assert.True(retained.Equals("a\"b"));
		Span<char> decoded = stackalloc char[retained.Length];
		Assert.True(retained.TryCopyTo(decoded));
		Assert.True(decoded.SequenceEqual("a\"b"));
	}
}
