using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandTokenizerTests
{
	[Fact]
	public void TokenizerNeedsNoRegistryAndPreservesDecodedTokens()
	{
		var tokenizer = new CommandTokenizer("give \"med;kit\" 2\r\nnext \"a\\\"b\"; empty \"\"");
		Assert.True(tokenizer.TryRead(out var give, out var result));
		Assert.True(result.IsSuccess);
		Assert.True(give.Name.Equals("give"));
		Assert.Equal(2, give.ArgumentCount);
		Assert.True(give.GetArgument(0).Equals("med;kit"));
		Assert.True(give.GetArgument(1).Equals("2"));
		Assert.True(tokenizer.TryRead(out var next, out result));
		Assert.True(next.GetArgument(0).Equals("a\"b"));
		Assert.True(give.GetArgument(0).Equals("med;kit"));
		Assert.True(tokenizer.TryRead(out var empty, out result));
		Assert.True(empty.GetArgument(0).Equals(""));
		Assert.False(tokenizer.TryRead(out _, out _));
		Assert.Throws<ArgumentOutOfRangeException>(() => give.GetArgument(2));
	}

	[Fact]
	public void SyntaxFailureCanBeReportedAndReadingContinuesAtNextLine()
	{
		var tokenizer = new CommandTokenizer("bad \"unfinished; ignored\nvalid 1");
		Assert.True(tokenizer.TryRead(out var invalid, out var result));
		Assert.False(invalid.IsValid);
		Assert.Equal(CommandErrorCode.InvalidSyntax, result.Error.Code);
		Assert.True(tokenizer.TryRead(out var valid, out result));
		Assert.True(result.IsSuccess);
		Assert.True(valid.Name.Equals("valid"));
		Assert.False(tokenizer.TryRead(out _, out _));
	}

	[Fact]
	public void ArrayInsertionSupportsHundredsOfCallsAndCopiesQueueElements()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry, 0);
		var commands = new CommandInvocation[350];
		for (var i = 0; i < commands.Length; i++)
		{
			var tokenizer = new CommandTokenizer($"add {i}");
			Assert.True(tokenizer.TryRead(out commands[i], out var result));
			Assert.True(result.IsSuccess);
		}

		processor.Enqueue(new Add(350));
		processor.Insert(commands);
		Array.Clear(commands);
		var seen = new List<int>();
		using var binding = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c =>
		{
			seen.Add(c.Value);
			return CommandResult.Ok();
		}));
		Assert.All(Collect(processor), r => Assert.True(r.IsSuccess));
		Assert.Equal(351, seen.Count);
		for (var i = 0; i < seen.Count; i++) Assert.Equal(i, seen[i]);
	}

	[Fact]
	public void PreparedCallsCanBeReusedAcrossProcessorsAndSingleAndBatchOverloads()
	{
		var tokenizer = new CommandTokenizer("add 7");
		tokenizer.TryRead(out var call, out _);
		var registry = new CommandRegistry();
		var first = new CommandProcessor(registry);
		var second = new CommandProcessor(registry);
		first.Insert(call);
		first.Enqueue(new[] { call });
		second.Enqueue(call);
		using var binding = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		Assert.All(Collect(first), r => Assert.Equal(7, r.Value.Get<int>()));
		Assert.Equal(7, Collect(second)[0].Value.Get<int>());
		Assert.True(call.GetArgument(0).Equals("7"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void EmptyCallsReportErrorsDuringExecutionAndDoNotStopFollowingCalls(bool insert)
	{
		var tokenizer = new CommandTokenizer("add 7");
		tokenizer.TryRead(out var call, out _);
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		var processor = new CommandProcessor(registry);
		if (insert)
		{
			processor.Insert(default(CommandInvocation));
			processor.Insert(new[] { call, default, call });
		}
		else
		{
			processor.Enqueue(default(CommandInvocation));
			processor.Enqueue(new[] { call, default, call });
		}

		var results = Collect(processor);
		Assert.Equal(4, results.Count);
		Assert.Equal(CommandErrorCode.InvalidSyntax, results[0].Error.Code);
		Assert.Equal(7, results[1].Value.Get<int>());
		Assert.Equal(CommandErrorCode.InvalidSyntax, results[2].Error.Code);
		Assert.Equal(7, results[3].Value.Get<int>());
		Assert.Empty(Collect(processor));
	}

	[Fact]
	public void LoadCommandCanRegisterLevelCommandsBeforeTheirExecution()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		IDisposable? level = null;
		using var load = registry.RegisterMethod(new("load", "", Array.Empty<CommandParameter>()), _ =>
		{
			level = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
			return CommandResult.Ok();
		});
		Assert.True(processor.Enqueue("load; add 2").IsSuccess);
		Assert.True(processor.Enqueue(new Add(3)).IsSuccess);
		try
		{
			var results = Collect(processor);
			Assert.Equal(3, results.Count);
			Assert.True(results[0].IsSuccess);
			Assert.Equal(2, results[1].Value.Get<int>());
			Assert.Equal(3, results[2].Value.Get<int>());
		}
		finally
		{
			level?.Dispose();
		}
	}

	[Fact]
	public void UnavailableTypedCallConsumesPayloadBeforeLaterRegistration()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue(new Add(1)).IsSuccess);
		Assert.Equal(CommandErrorCode.Unavailable, Collect(processor)[0].Error.Code);
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		processor.Enqueue(new Add(2));
		Assert.Equal(2, Collect(processor)[0].Value.Get<int>());
	}

	[Fact]
	public void TextArgumentsAreCheckedAgainstReplacementSchemaAtExecution()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var original = registry.RegisterMethod(new("setting", "", new[] { new CommandParameter("value", typeof(int)) }),
			_ => CommandResult.Ok(1));
		Assert.True(processor.Enqueue("setting true").IsSuccess);
		original.Dispose();
		using var replacement = registry.RegisterMethod(new("setting", "", new[] { new CommandParameter("value", typeof(bool)) }),
			a => CommandResult.Ok(a.Get<bool>(0)));
		Assert.True(Collect(processor)[0].Value.Get<bool>());
	}

	[Fact]
	public void FailedConvenienceInsertionDoesNotInsertPrefixOrDamageExistingTokens()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		processor.Enqueue("add 3");
		Assert.Equal(CommandErrorCode.InvalidSyntax, processor.Insert("add 1; bad \"quote").Error.Code);
		Assert.True(processor.Insert("add 2").IsSuccess);
		var results = Collect(processor);
		Assert.Equal(2, results.Count);
		Assert.Equal(2, results[0].Value.Get<int>());
		Assert.Equal(3, results[1].Value.Get<int>());
	}

	[Fact]
	public void SyntaxRejectionLeavesQueueUntouchedWhileSemanticFailuresAreIndependent()
	{
		var processor = new CommandProcessor(new CommandRegistry());
		Assert.False(processor.Enqueue("unknown; broken \"quote").IsSuccess);
		Assert.Empty(Collect(processor));
		Assert.True(processor.Enqueue("unknown; another").IsSuccess);
		var results = Collect(processor);
		Assert.Equal(2, results.Count);
		Assert.All(results, r => Assert.Equal(CommandErrorCode.UnknownCommand, r.Error.Code));
	}

	[Theory]
	[InlineData("add 1\nadd 2", CommandErrorCode.None)]
	[InlineData("add 1\r\nadd 2", CommandErrorCode.None)]
	[InlineData("add \"unfinished\nadd 2", CommandErrorCode.InvalidSyntax)]
	[InlineData(" ; \n ", CommandErrorCode.InvalidSyntax)]
	public void StringOverloadsUseIdenticalGrammar(string text, CommandErrorCode expected)
	{
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		var enqueue = new CommandProcessor(registry);
		var insert = new CommandProcessor(registry);
		Assert.Equal(expected, enqueue.Enqueue(text).Error.Code);
		Assert.Equal(expected, insert.Insert(text).Error.Code);
		var enqueued = Collect(enqueue);
		var inserted = Collect(insert);
		Assert.Equal(enqueued, inserted);
		Assert.Equal(expected == CommandErrorCode.None ? 2 : 0, enqueued.Count);
	}

	[Fact]
	public void PreparingAnotherBatchBeforeExecutionDoesNotReleaseQueuedTokens()
	{
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c => CommandResult.Ok(c.Value)));
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("add 10; add 20").IsSuccess);
		Assert.Equal(CommandErrorCode.InvalidSyntax, processor.Insert("add 99; bad \"quote").Error.Code);
		Assert.True(processor.Insert("add 1; add 2").IsSuccess);
		Assert.True(processor.Enqueue("add 30").IsSuccess);
		var results = Collect(processor);
		Assert.Equal(5, results.Count);
		var expected = new[] { 1, 2, 10, 20, 30 };
		for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], results[i].Value.Get<int>());
	}
}
