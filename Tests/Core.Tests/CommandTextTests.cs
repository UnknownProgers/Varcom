using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandTextTests
{
	[Theory]
	[InlineData("pair bad 2 3", CommandErrorCode.InvalidArguments, "Too many arguments.", null)]
	[InlineData("pair bad", CommandErrorCode.InvalidArguments, "Expected Int32 for 'first'.", "first")]
	[InlineData("pair 1", CommandErrorCode.InvalidArguments, "Missing argument 'second'.", "second")]
	[InlineData("pair bad \"unfinished", CommandErrorCode.InvalidSyntax, "Unterminated quoted string.", null)]
	[InlineData("readonly bad extra", CommandErrorCode.ReadOnly, "Variable 'readonly' is read-only.", null)]
	[InlineData("readonly bad \"unfinished", CommandErrorCode.InvalidSyntax, "Unterminated quoted string.", null)]
	public void EnqueueRejectsSyntaxButDefersArgumentDiagnostics(string text, CommandErrorCode code,
		string message, string? argumentName)
	{
		var registry = new CommandRegistry();
		var calls = 0;
		using var command = registry.RegisterMethod(new("pair", "", new[]
		{
			new CommandParameter("first", typeof(int)), new CommandParameter("second", typeof(int))
		}), _ =>
		{
			calls++;
			return CommandResult.Ok();
		});
		using var variable = registry.RegisterVariable(new("readonly", "", typeof(int), true), () => 1);
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("pair 1 2").IsSuccess);

		var result = processor.Enqueue("pair 1 2; " + text);
		if (code == CommandErrorCode.InvalidSyntax)
		{
			Assert.Equal(code, result.Error.Code);
			Assert.Equal(message, result.Error.Message);
			processor.Update();
			Assert.Equal(1, calls);
		}
		else
		{
			Assert.True(result.IsSuccess);
			var results = Collect(processor);
			Assert.Equal(code, results[2].Error.Code);
			Assert.Equal(message, results[2].Error.Message);
			Assert.Equal(argumentName, results[2].Error.ArgumentName);
			Assert.Equal(2, calls);
		}
	}

	[Theory]
	[InlineData("echo plain", "plain")]
	[InlineData("echo \"\"", "")]
	[InlineData("echo pre\"mid dle\"post", "premid dlepost")]
	[InlineData("echo \"a\"\"b\"", "ab")]
	[InlineData("echo \"a;b\"", "a;b")]
	[InlineData("echo a\\b", "a\\b")]
	[InlineData("echo \"a\\qb\"", "a\\qb")]
	[InlineData("echo \"a\\\\b\"", "a\\b")]
	[InlineData("echo \"a\\\"b\"", "a\"b")]
	[InlineData(" ;\t echo\u2003\"привет мир\" ; ; ", "привет мир")]
	public void Enqueue_PreservesTokenContents(string text, string expected)
	{
		var registry = new CommandRegistry();
		string? received = null;
		using var registration = registry.RegisterMethod(new("echo", "", new[] { new CommandParameter("text", typeof(CmdString)) }),
			args =>
			{
				received = args.Get<CmdString>(0).ToString();
				return CommandResult.Ok();
			});
		var processor = new CommandProcessor(registry);

		Assert.True(processor.Enqueue(text).IsSuccess);
		processor.Update();

		Assert.Equal(expected, received);
	}

	[Fact]
	public void Enqueue_ValidatesArgumentsAndAppliesOptionalDefaults()
	{
		var registry = new CommandRegistry();
		var value = 0;
		using var binding = registry.Register(Info(), a => new Add(a.Get<int>(0), a.Get<int>(1)),
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
		Assert.True(processor.Enqueue("missing").IsSuccess);
		Assert.Equal(CommandErrorCode.UnknownCommand, Collect(processor)[0].Error.Code);
	}

	[Theory]
	[InlineData("add")]
	[InlineData("add nope")]
	[InlineData("add 1 2 3")]
	[InlineData("add 999999999999")]
	public void UpdateRejectsInvalidArgumentsWithoutCallingHandler(string text)
	{
		var registry = new CommandRegistry();
		var calls = 0;
		using var registration = registry.Register(Info(), args => new Add(args.Get<int>(0)),
			new Handler(_ =>
			{
				calls++;
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);

		var result = processor.Enqueue(text);

		Assert.True(result.IsSuccess);
		Assert.Equal(CommandErrorCode.InvalidArguments, Collect(processor)[0].Error.Code);
		Assert.Equal(0, calls);
	}

	[Fact]
	public void Enqueue_HandlesQuotesAndMultipleCommands()
	{
		var registry = new CommandRegistry();
		string? received = null;
		using var registration = registry.RegisterMethod(
			new CommandInfo("echo", "Echo", new[] { new CommandParameter("text", typeof(CmdString)) }),
			a =>
			{
				received = a.Get<CmdString>(0).ToString();
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
		using var registration = registry.RegisterMethod(new("echo", "", new[] { new CommandParameter("text", typeof(CmdString)) }),
			a =>
			{
				seen.Add(a.Get<CmdString>(0).ToString());
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
		using var child = registry.Register(Info(), args => new Add(args.Get<int>(0)),
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
		using var registration = registry.Register(Info(), args => new Add(args.Get<int>(0)),
			new Handler(c =>
			{
				seen.Add(c.Value);
				return CommandResult.Ok();
			}));
		var processor = new CommandProcessor(registry);
		processor.Enqueue(new Add(10));
		foreach (var invalid in new[] { "add 1; add \"unterminated", " ; ; " })
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
		using var child = registry.Register(Info(), args => new Add(args.Get<int>(0)),
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
