using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandInsertionTests
{
	[Fact]
	public void ExecInsertsNestedScriptsBeforeTypedAndTextRemainder()
	{
		using var fixture = new ExecFixture();
		fixture.Scripts["outer"] = "add 2; exec inner; add 4";
		fixture.Scripts["inner"] = "add 3";
		fixture.Processor.Enqueue("add 1; exec outer; add 5");
		fixture.Processor.Enqueue(new Add(6));
		var results = Collect(fixture.Processor);
		Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, fixture.Seen);
		Assert.Equal(8, results.Count);
		Assert.All(results, result => Assert.True(result.IsSuccess));
		Assert.Empty(Collect(fixture.Processor));
	}

	[Fact]
	public void ExecContinuesAfterIndependentParseAndExecutionFailures()
	{
		using var fixture = new ExecFixture();
		fixture.Scripts["errors"] = "add 1; unknown; add nope; add -1; add \"unterminated\nadd 2";
		fixture.Processor.Enqueue("exec errors; add 3");
		var results = Collect(fixture.Processor);
		Assert.Equal(new[] { 1, -1, 2, 3 }, fixture.Seen);
		Assert.Equal(CommandErrorCode.UnknownCommand, results[2].Error.Code);
		Assert.False(results[3].IsSuccess);
		Assert.Equal(CommandErrorCode.ExecutionFailed, results[4].Error.Code);
		Assert.Single(fixture.SyntaxErrors);
		Assert.True(results[5].IsSuccess);
		Assert.True(results[6].IsSuccess);
	}

	[Fact]
	public void MissingAndEmptyScriptsAllowOuterSequenceToContinue()
	{
		using var fixture = new ExecFixture();
		fixture.Scripts["empty"] = " ; \r\n;\t";
		fixture.Processor.Enqueue("exec missing; exec empty; add 1");
		var results = Collect(fixture.Processor);
		Assert.Equal(3, results.Count);
		Assert.False(results[0].IsSuccess);
		Assert.True(results[1].IsSuccess);
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void RepeatedInsertionsPreserveOrderAndEnqueueStillWaitsForNextUpdate()
	{
		using var fixture = new ExecFixture();
		using var registration = fixture.Registry.RegisterMethod(new("insert", "", Array.Empty<CommandParameter>()), _ =>
		{
			fixture.Processor.Insert("add 1");
			fixture.Processor.Insert("add 2");
			fixture.Processor.Enqueue("add 4");
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("insert; add 3");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4 }, fixture.Seen);
	}

	[Fact]
	public void InsertOutsideUpdatePreservesOrderAndNullDoesNotModifyPendingWork()
	{
		using var fixture = new ExecFixture();
		fixture.Processor.Enqueue(new Add(3));
		fixture.Processor.Insert("add 1");
		Assert.ThrowsAny<Exception>(() => fixture.Processor.Insert((string)null!));
		fixture.Processor.Insert("add 2");
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3 }, fixture.Seen);
	}

	[Fact]
	public void CallbackExceptionPreservesScriptContinuationAndOriginalQueue()
	{
		using var fixture = new ExecFixture();
		fixture.Scripts["script"] = "add 1; add 2";
		fixture.Processor.Enqueue("exec script; add 3");
		var count = 0;
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ =>
		{
			if (++count == 2) throw new InvalidOperationException("Output failed");
		}));
		Assert.Equal(new[] { 1 }, fixture.Seen);
		Assert.Equal(2, Collect(fixture.Processor).Count);
		Assert.Equal(new[] { 1, 2, 3 }, fixture.Seen);
	}

	[Fact]
	public void ScriptResolvesRegistrationsOnlyWhenEachCommandIsReached()
	{
		using var fixture = new ExecFixture();
		IDisposable? dynamicRegistration = null;
		using var enable = fixture.Registry.RegisterMethod(new("enable", "", Array.Empty<CommandParameter>()), _ =>
		{
			dynamicRegistration =
				fixture.Registry.RegisterMethod(new("dynamic", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok(7));
			return CommandResult.Ok();
		});
		using var disable = fixture.Registry.RegisterMethod(new("disable", "", Array.Empty<CommandParameter>()), _ =>
		{
			dynamicRegistration!.Dispose();
			return CommandResult.Ok();
		});
		fixture.Processor.Insert("enable; dynamic; disable; dynamic; add 1");
		var results = Collect(fixture.Processor);
		Assert.Equal(7, results[1].Value.Get<int>());
		Assert.Equal(CommandErrorCode.UnknownCommand, results[3].Error.Code);
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void ScriptSeparatorsRespectQuotedAndEscapedText()
	{
		using var fixture = new ExecFixture();
		var text = new List<string>();
		using var echo = fixture.Registry.RegisterMethod(new("echo", "", new[] { new CommandParameter("text", typeof(CmdString)) }), args =>
		{
			text.Add(args.Get<CmdString>(0).ToString());
			return CommandResult.Ok();
		});
		fixture.Processor.Insert("echo \"a; b\\\"c\\\\d\";\r\necho \"\"\necho end");
		Assert.All(Collect(fixture.Processor), result => Assert.True(result.IsSuccess));
		Assert.Equal(new[] { "a; b\"c\\d", "", "end" }, text);
	}

	[Fact]
	public void TypedPayloadsRemainAlignedWhenInsertedHandlerEnqueuesSameType()
	{
		using var fixture = new ExecFixture();
		using var enqueue = fixture.Registry.RegisterMethod(new("enqueue", "", Array.Empty<CommandParameter>()), _ =>
		{
			fixture.Processor.Enqueue(new Add(3));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue(new Add(1));
		fixture.Processor.Enqueue(new Add(2));
		fixture.Processor.Insert("enqueue");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3 }, fixture.Seen);
	}

	[Fact]
	public void GrowingQueueReusesNodesWithoutChangingInsertionOrder()
	{
		using var fixture = new ExecFixture();
		for (var round = 0; round < 3; round++)
		{
			for (var i = 0; i < 300; i++)
				fixture.Processor.Enqueue(new Add(i));
			fixture.Processor.Insert("add 700; add 701");
			fixture.Processor.Update();
			Assert.Equal(700, fixture.Seen[round * 302]);
			Assert.Equal(701, fixture.Seen[round * 302 + 1]);
			for (var i = 0; i < 300; i++)
				Assert.Equal(i, fixture.Seen[round * 302 + i + 2]);
		}
	}

	[Fact]
	public void CallbackCanInsertAndEnqueueThenThrowWithoutLosingEither()
	{
		using var fixture = new ExecFixture();
		fixture.Processor.Insert("add 1; add 3");
		fixture.Processor.Enqueue(new Add(4));
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ =>
		{
			fixture.Processor.Insert("add 2");
			fixture.Processor.Enqueue(new Add(5));
			throw new InvalidOperationException("Output failed");
		}));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4, 5 }, fixture.Seen);
		fixture.Processor.Enqueue(new Add(6));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, fixture.Seen);
	}

	[Fact]
	public void EnqueuedAndInsertedCommandsBothResolveReplacement()
	{
		using var fixture = new ExecFixture();
		var info = new CommandInfo("dynamic", "", Array.Empty<CommandParameter>());
		var previous = fixture.Registry.RegisterMethod(info, _ => CommandResult.Ok(1));
		fixture.Processor.Enqueue("dynamic");
		fixture.Processor.Insert("dynamic");
		previous.Dispose();
		using var replacement = fixture.Registry.RegisterMethod(info, _ => CommandResult.Ok(2));
		var results = Collect(fixture.Processor);
		Assert.Equal(2, results[0].Value.Get<int>());
		Assert.Equal(2, results[1].Value.Get<int>());
	}

	private sealed class ExecFixture : IDisposable
	{
		internal CommandRegistry Registry { get; } = new();
		internal CommandProcessor Processor { get; }
		internal Dictionary<string, string> Scripts { get; } = new();
		internal List<int> Seen { get; } = new();
		internal List<CommandError> SyntaxErrors { get; } = new();
		private readonly IDisposable _add;
		private readonly IDisposable _exec;

		internal ExecFixture()
		{
			Processor = new CommandProcessor(Registry);
			_add = Registry.Register(Info(), args => new Add(args.Get<int>(0)), new Handler(command =>
			{
				Seen.Add(command.Value);
				return command.Value < 0 ? CommandResult.Fail("Rejected") : CommandResult.Ok(command.Value);
			}));
			_exec = Registry.RegisterMethod(new("exec", "", new[] { new CommandParameter("source", typeof(CmdString)) }), args =>
			{
				if (!Scripts.TryGetValue(args.Get<CmdString>(0).ToString(), out var text))
					return CommandResult.Fail("Script not found.");
				var tokenizer = new CommandTokenizer(text);
				while (tokenizer.TryRead(out var invocation, out var result))
				{
					if (result.IsSuccess) Processor.Insert(invocation);
					else SyntaxErrors.Add(result.Error);
				}

				return CommandResult.Ok();
			});
		}

		public void Dispose()
		{
			_exec.Dispose();
			_add.Dispose();
		}
	}
}
