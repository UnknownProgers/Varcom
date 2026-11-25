using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandTextTests
{
	[Fact]
	public void Enqueue_ValidatesArgumentsAndAppliesOptionalDefaults()
	{
		var registry = new CommandRegistry();
		var value = 0;
		using var binding = registry.Register(Info(), a => new Add((int)a[0]!, (int)a[1]!),
			new Handler(c =>
			{
				value += c.Value * c.Count;
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("ADD 7").IsSuccess);
		Assert.Equal(0, value);
		processor.Update();
		Assert.Equal(7, value);
		Assert.False(processor.Enqueue("missing").IsSuccess);
	}

	[Theory]
	[InlineData("add")]
	[InlineData("add nope")]
	[InlineData("add 1 2 3")]
	[InlineData("add 999999999999")]
	public void Enqueue_RejectsInvalidArgumentsWithoutExecuting(string text)
	{
		var registry = new CommandRegistry();
		var calls = 0;
		using var registration = registry.Register(Info(), args => new Add((int)args[0]!),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);

		var result = processor.Enqueue(text);

		Assert.False(result.IsSuccess);
		Assert.Equal(CommandErrorCode.InvalidArguments, result.Error.Code);
		Assert.Empty(Collect(processor));
		Assert.Equal(0, calls);
	}

	[Fact]
	public void Enqueue_HandlesQuotesAndMultipleCommands()
	{
		var registry = new CommandRegistry();
		string? received = null;
		using var registration = registry.RegisterMethod(
			new CommandInfo("echo", "Echo", new[] { new CommandParameter("text", typeof(string)) }),
			a =>
			{
				received = (string)a[0]!;
				return CommandResult.Ok();
			});
		var processor = new CommandProcessor(registry);
		processor.Enqueue("echo \"\"");
		processor.Update();
		Assert.Equal("", received);
		processor.Enqueue("echo \"a b\"");
		processor.Update();
		Assert.Equal("a b", received);
		Assert.False(processor.Enqueue("echo \"unfinished").IsSuccess);
		Assert.True(processor.Enqueue("echo a; echo b").IsSuccess);
		processor.Update();
		Assert.Equal("b", received);
	}

	[Fact]
	public void Enqueue_PreservesQuotedSemicolonsEmptyStringsAndEscapes()
	{
		var registry = new CommandRegistry();
		var seen = new List<string>();
		using var registration = registry.RegisterMethod(new("echo", "", new[] { new CommandParameter("text", typeof(string)) }),
			a =>
			{
				seen.Add((string)a[0]!);
				return CommandResult.Ok();
			});
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue(" ; echo \"a;b\"; echo \"\";; echo \"a\\\"b\"; ").IsSuccess);
		Assert.Empty(seen);
		processor.Update();
		Assert.Equal(new[] { "a;b", "", "a\"b" }, seen);
	}

	[Fact]
	public void Update_DefersTypedAndTextChildren()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var order = new List<int>();
		using var child = registry.Register(Info(), args => new Add((int)args[0]!),
			new Handler(c =>
			{
				order.Add(c.Value);
				return CommandResult.Ok();
			}));
		using var parent = registry.RegisterMethod(new CommandInfo("parent", "", Array.Empty<CommandParameter>()), _ =>
		{
			order.Add(1);
			processor.Enqueue(new Add(3));
			processor.Enqueue("add 4");
			return CommandResult.Ok();
		});
		processor.Enqueue("parent");
		processor.Enqueue(new Add(2));
		processor.Update();
		Assert.Equal(new[] { 1, 2 }, order);
		processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4 }, order);
	}

	[Fact]
	public void Enqueue_RejectsInvalidSequenceWithoutChangingExistingQueue()
	{
		var registry = new CommandRegistry();
		var seen = new List<int>();
		using var registration = registry.Register(Info(), args => new Add((int)args[0]!),
			new Handler(c =>
			{
				seen.Add(c.Value);
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(10));
		foreach (var invalid in new[] { "add 1; missing; add 3", "add 1; add nope", "add 1; add \"unterminated", " ; ; " })
			Assert.False(processor.Enqueue(invalid).IsSuccess);
		processor.Update();
		Assert.Equal(new[] { 10 }, seen);
		Assert.True(processor.Enqueue("add 2; add 3").IsSuccess);
		processor.Update();
		Assert.Equal(new[] { 10, 2, 3 }, seen);
	}

	[Fact]
	public void Update_DefersChildCommandSequences()
	{
		var registry = new CommandRegistry();
		var seen = new List<int>();
		var processor = new CommandProcessor(registry);
		using var child = registry.Register(Info(), args => new Add((int)args[0]!),
			new Handler(c =>
			{
				seen.Add(c.Value);
				return CommandResult.Ok();
			}));
		using var parent = registry.RegisterMethod(new("parent", "", Array.Empty<CommandParameter>()), _ =>
		{
			Assert.True(processor.Enqueue("add 3; add 4").IsSuccess);
			return CommandResult.Ok();
		});
		processor.Enqueue("parent; add 2");
		processor.Update();
		Assert.Equal(new[] { 2 }, seen);
		processor.Update();
		Assert.Equal(new[] { 2, 3, 4 }, seen);
	}
}
