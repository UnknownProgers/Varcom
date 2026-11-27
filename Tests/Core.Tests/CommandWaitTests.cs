using System;
using System.Collections.Generic;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CommandWaitTests
{
	[Fact]
	public void WaitStopsBeforeNextCommandAndResumesAtRequestedUpdate()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1; wait 3; add 2");
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.Equal(new[] { 1 }, fixture.Seen);
		Assert.Equal(2, results.Count);
		Assert.True(results[1].IsSuccess);
		fixture.Processor.Update(results.Add);
		fixture.Processor.Update(results.Add);
		Assert.Equal(2, results.Count);
		fixture.Processor.Update(results.Add);
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
		Assert.Equal(3, results.Count);
	}

	[Theory]
	[InlineData(0, CommandErrorCode.None)]
	[InlineData(-1, CommandErrorCode.InvalidArguments)]
	public void ZeroOrInvalidDelayDoesNotStopQueue(int delay, CommandErrorCode code)
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue($"wait {delay}; add 2");
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.Equal(code, results[0].Error.Code);
		Assert.Equal(new[] { 2 }, fixture.Seen);
	}

	[Fact]
	public void IndependentAdditionsRunWhileOriginalSequenceWaits()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 3; add 3");
		fixture.Processor.Update();
		fixture.Processor.Enqueue(new Add(4));
		fixture.Processor.Insert("add 1");
		fixture.Processor.Insert("add 2");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 4 }, fixture.Seen);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 4, 3 }, fixture.Seen);
	}

	[Fact]
	public void WaitInsideExecPreservesScriptAndOuterRemainder()
	{
		using var fixture = new Fixture();
		using var exec = fixture.Registry.RegisterMethod(new("exec", "", Array.Empty<CommandParameter>()), _ =>
		{
			var tokenizer = new CommandTokenizer("add 1; wait 2; add 2");
			while (tokenizer.TryRead(out var command, out var result))
				if (result.IsSuccess)
					fixture.Processor.Insert(command);
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("exec; add 3");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3 }, fixture.Seen);
	}

	[Fact]
	public void ConsecutiveWaitsEachCountTheirOwnUpdates()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 2; add 1; wait 3; add 2");
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
	}

	[Fact]
	public void WaitOneResumesOnNextUpdate()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 1; add 1");
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void CallbackFailurePreservesWaitAndRemainingCommands()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 2; add 1");
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ => throw new InvalidOperationException()));
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void CommandsCanBecomeAvailableWhileWaiting()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 2; level");
		fixture.Processor.Update();
		using var level = fixture.Registry.RegisterMethod(new("level", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok(7));
		var results = new List<CommandResult>();
		fixture.Processor.Update();
		fixture.Processor.Update(results.Add);
		Assert.Equal(7, Assert.Single(results).Value.Get<int>());
	}

	[Fact]
	public void EnqueueFromWaitHandlerStillDefersUntilAnotherUpdate()
	{
		using var fixture = new Fixture();
		using var parent = fixture.Registry.RegisterMethod(new("parent", "", Array.Empty<CommandParameter>()), _ =>
		{
			fixture.Processor.Enqueue(new Add(3));
			return fixture.Processor.WaitFrames(2);
		});
		fixture.Processor.Enqueue("parent; add 2");
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 3 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 3, 2 }, fixture.Seen);
	}

	[Fact]
	public void WaitRequestsOutsideExecutionFailWithoutDelayingQueuedWork()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1");
		Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.WaitFrames(2).Error.Code);
		Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.Suspend(out var token).Error.Code);
		Assert.False(fixture.Processor.Resume(token));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void IndependentSuspensionsHaveSeparateTokensAndIgnoreStaleOrForeignSignals()
	{
		using var fixture = new Fixture();
		var tokens = new List<CommandSuspension>();
		using var ready = fixture.Registry.RegisterMethod(new("ready", "", Array.Empty<CommandParameter>()), _ =>
		{
			tokens.Add(Suspend(fixture.Processor));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("ready; add 1");
		fixture.Processor.Enqueue("ready; add 2");
		fixture.Processor.Update();
		Assert.Equal(2, tokens.Count);
		Assert.False(new CommandProcessor(fixture.Registry).Resume(tokens[0]));
		Assert.False(fixture.Processor.Resume(default));
		Assert.True(fixture.Processor.Resume(tokens[1]));
		Assert.False(fixture.Processor.Resume(tokens[1]));
		fixture.Processor.Update();
		Assert.Equal(new[] { 2 }, fixture.Seen);
		// Reuse the completed sequence's storage; its old signal must remain invalid.
		fixture.Processor.Enqueue("ready; add 3");
		fixture.Processor.Update();
		Assert.False(fixture.Processor.Resume(tokens[1]));
		Assert.True(fixture.Processor.Resume(tokens[0]));
		fixture.Processor.Update();
		Assert.Equal(new[] { 2, 1 }, fixture.Seen);
		Assert.True(fixture.Processor.Resume(tokens[2]));
		fixture.Processor.Update();
		Assert.Equal(new[] { 2, 1, 3 }, fixture.Seen);
	}

	[Fact]
	public void ImmediateExternalSignalStillStopsTheCurrentBatch()
	{
		using var fixture = new Fixture();
		using var wait = fixture.Registry.RegisterMethod(new("ready", "", Array.Empty<CommandParameter>()), _ =>
		{
			Assert.True(fixture.Processor.Resume(Suspend(fixture.Processor)));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("ready; add 1");
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void SignalHandlerCanWaitForNewlyRegisteredLevelCommands()
	{
		using var fixture = new Fixture();
		CommandSuspension suspension = default;
		using var ready = fixture.Registry.RegisterMethod(new("ready", "", Array.Empty<CommandParameter>()), _ =>
		{
			suspension = Suspend(fixture.Processor);
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("ready; level");
		fixture.Processor.Update();
		fixture.Processor.Update();
		using var level = fixture.Registry.RegisterMethod(new("level", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok(7));
		Assert.True(fixture.Processor.Resume(suspension));
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.Equal(7, Assert.Single(results).Value.Get<int>());
	}

	[Fact]
	public void FrameWaitFailsWithoutInvalidatingExternalToken()
	{
		using var fixture = new Fixture();
		CommandSuspension token = default;
		using var replace = fixture.Registry.RegisterMethod(new("replace", "", Array.Empty<CommandParameter>()), _ =>
		{
			token = Suspend(fixture.Processor);
			return fixture.Processor.WaitFrames(1);
		});
		fixture.Processor.Enqueue("replace; add 1");
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.Equal(CommandErrorCode.ExecutionFailed, Assert.Single(results).Error.Code);
		Assert.True(fixture.Processor.Resume(token));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void CallbackFailurePreservesExternalSuspension()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1; add 2");
		CommandSuspension token = default;
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ =>
		{
			token = Suspend(fixture.Processor);
			throw new InvalidOperationException();
		}));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
		Assert.True(fixture.Processor.Resume(token));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
	}

	[Fact]
	public void SignalAndFrameWaitConflictWithinTheSameSequence()
	{
		using var fixture = new Fixture();
		using var both = fixture.Registry.RegisterMethod(new("both", "", Array.Empty<CommandParameter>()), _ =>
		{
			Assert.True(fixture.Processor.WaitFrames(3).IsSuccess);
			Assert.False(fixture.Processor.Suspend(out var rejected).IsSuccess);
			Assert.False(fixture.Processor.Resume(rejected));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("both; add 1");
		fixture.Processor.Update();
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void SecondExternalSuspensionInSameHandlerFailsAndOriginalTokenRemainsActive()
	{
		using var fixture = new Fixture();
		CommandSuspension original = default;
		using var both = fixture.Registry.RegisterMethod(new("both", "", Array.Empty<CommandParameter>()), _ =>
		{
			original = Suspend(fixture.Processor);
			Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.Suspend(out var rejected).Error.Code);
			Assert.False(fixture.Processor.Resume(rejected));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("both; add 1");
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		Assert.True(fixture.Processor.Resume(original));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void SecondFrameWaitFailsAndDoesNotShortenOriginalWait()
	{
		using var fixture = new Fixture();
		using var wait = fixture.Registry.RegisterMethod(new("double_wait", "", Array.Empty<CommandParameter>()), _ =>
		{
			Assert.True(fixture.Processor.WaitFrames(3).IsSuccess);
			Assert.False(fixture.Processor.WaitFrames(0).IsSuccess);
			return fixture.Processor.WaitFrames(1);
		});
		fixture.Processor.Enqueue("double_wait; add 1");
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.Equal(CommandErrorCode.ExecutionFailed, Assert.Single(results).Error.Code);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void TenSequencesWaitIndependentlyAndRunInSubmissionOrderWhenReady()
	{
		using var fixture = new Fixture();
		for (var i = 1; i <= 10; i++)
			fixture.Processor.Enqueue($"add {i}; wait {i}; add {100 + i}");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, fixture.Seen);
		for (var frame = 1; frame <= 10; frame++)
		{
			fixture.Processor.Update();
			Assert.Equal(10 + frame, fixture.Seen.Count);
			Assert.Equal(100 + frame, fixture.Seen[^1]);
		}
	}

	[Fact]
	public void WaitWithoutContinuationDoesNotDelayFutureSubmissions()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 100");
		fixture.Processor.Update();
		fixture.Processor.Enqueue("add 1");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void ResumingAnotherSequenceFromHandlerDefersItUntilNextUpdate()
	{
		using var fixture = new Fixture();
		CommandSuspension token = default;
		using var ready = fixture.Registry.RegisterMethod(new("ready", "", Array.Empty<CommandParameter>()), _ =>
		{
			token = Suspend(fixture.Processor);
			return CommandResult.Ok();
		});
		using var signal = fixture.Registry.RegisterMethod(new("signal", "", Array.Empty<CommandParameter>()), _ =>
		{
			Assert.True(fixture.Processor.Resume(token));
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("ready; add 1");
		fixture.Processor.Update();
		fixture.Processor.Insert("signal; add 2");
		fixture.Processor.Update();
		Assert.Equal(new[] { 2 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 2, 1 }, fixture.Seen);
	}

	[Fact]
	public void WaitingTailStillCountsFramesWhenAnotherCallbackThrows()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 2; add 2");
		fixture.Processor.Update();
		fixture.Processor.Insert("add 1");
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ => throw new InvalidOperationException()));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
	}

	[Fact]
	public void TimelineOfSixSequencesAndNewInputMatchesIndependentContinuations()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 11; wait 2; add 12; wait 2; add 13");
		fixture.Processor.Enqueue("add 21; wait 1; add 22");
		fixture.Processor.Enqueue("add 31; wait 4; add 32");
		fixture.Processor.Enqueue("add 41; wait 2; add 42");
		fixture.Processor.Enqueue("add 51; wait 3; add 52");
		fixture.Processor.Enqueue("add 61; wait 1; add 62; wait 2; add 63");
		fixture.Processor.Update();
		Assert.Equal(new[] { 11, 21, 31, 41, 51, 61 }, fixture.Seen);
		fixture.Seen.Clear();
		fixture.Processor.Enqueue("add 99");
		fixture.Processor.Update();
		Assert.Equal(new[] { 22, 62, 99 }, fixture.Seen);
		fixture.Seen.Clear();
		fixture.Processor.Update();
		Assert.Equal(new[] { 12, 42 }, fixture.Seen);
		fixture.Seen.Clear();
		fixture.Processor.Update();
		Assert.Equal(new[] { 52, 63 }, fixture.Seen);
		fixture.Seen.Clear();
		fixture.Processor.Update();
		Assert.Equal(new[] { 13, 32 }, fixture.Seen);
	}

	[Fact]
	public void TokenizedBatchIsOneSequenceButSeparateCallsAreIndependent()
	{
		using var fixture = new Fixture();
		var tokenizer = new CommandTokenizer("wait 2; add 1");
		var batch = new List<CommandInvocation>();
		while (tokenizer.TryRead(out var command, out var result))
		{
			Assert.True(result.IsSuccess);
			batch.Add(command);
		}

		fixture.Processor.Enqueue(batch.ToArray());
		foreach (var command in batch) fixture.Processor.Enqueue(command);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 1 }, fixture.Seen);
	}

	[Fact]
	public void NestedExecWaitsDelayOuterTailButNotIndependentWork()
	{
		using var fixture = new Fixture();
		using var outer = fixture.Registry.RegisterMethod(new("outer", "", Array.Empty<CommandParameter>()), _ =>
			fixture.Processor.Insert("add 1; inner; add 3"));
		using var inner = fixture.Registry.RegisterMethod(new("inner", "", Array.Empty<CommandParameter>()), _ =>
			fixture.Processor.Insert("wait 2; add 2"));
		fixture.Processor.Enqueue("outer; add 4");
		fixture.Processor.Enqueue(new Add(5));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 5 }, fixture.Seen);
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 5, 2, 3, 4 }, fixture.Seen);
	}

	[Fact]
	public void StandaloneSuspensionDoesNotRetainAnActiveToken()
	{
		using var fixture = new Fixture();
		CommandSuspension token = default;
		using var ready = fixture.Registry.RegisterMethod(new("ready", "", Array.Empty<CommandParameter>()), _ =>
		{
			token = Suspend(fixture.Processor);
			return CommandResult.Ok();
		});
		fixture.Processor.Enqueue("ready");
		fixture.Processor.Update();
		Assert.False(fixture.Processor.Resume(token));
		fixture.Processor.Enqueue(new Add(1));
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void CallbackFailureAfterInsertionPreservesTailAndClearsActiveSequence()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1; add 3");
		fixture.Processor.Enqueue("add 4");
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ =>
		{
			fixture.Processor.Insert("add 2");
			throw new InvalidOperationException();
		}));
		Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.WaitFrames(1).Error.Code);
		fixture.Processor.Insert("add 0");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 0, 2, 3, 4 }, fixture.Seen);
	}

	[Fact]
	public void CallbackFailureOnLastCommandReleasesSequenceAndInvalidatesToken()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1");
		CommandSuspension token = default;
		Assert.Throws<InvalidOperationException>(() => fixture.Processor.Update(_ =>
		{
			token = Suspend(fixture.Processor);
			throw new InvalidOperationException();
		}));
		Assert.False(fixture.Processor.Resume(token));
		Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.Suspend(out _).Error.Code);
		fixture.Processor.Insert("add 2");
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
	}

	[Fact]
	public void ThrowingHandlerPreservesItsWaitAndDoesNotBlockIndependentWork()
	{
		using var fixture = new Fixture();
		using var broken = fixture.Registry.RegisterMethod(new("broken", "", Array.Empty<CommandParameter>()), _ =>
		{
			fixture.Processor.WaitFrames(2);
			throw new InvalidOperationException("Handler failed after requesting wait.");
		});
		fixture.Processor.Enqueue("broken; add 1");
		fixture.Processor.Enqueue("add 2");
		var results = new List<CommandResult>();
		fixture.Processor.Update(results.Add);
		Assert.False(results[0].IsSuccess);
		Assert.Equal(new[] { 2 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 2 }, fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 2, 1 }, fixture.Seen);
	}

	private static CommandSuspension Suspend(CommandProcessor processor)
	{
		Assert.True(processor.Suspend(out var token).IsSuccess);
		return token;
	}

	private sealed class Fixture : IDisposable
	{
		internal CommandRegistry Registry { get; } = new();
		internal CommandProcessor Processor { get; }
		internal List<int> Seen { get; } = new();
		private readonly IDisposable _add;
		private readonly IDisposable _wait;

		internal Fixture()
		{
			Processor = new CommandProcessor(Registry);
			_add = Registry.Register(Info(), a => new Add(a.Get<int>(0)), new Handler(c =>
			{
				Seen.Add(c.Value);
				return CommandResult.Ok();
			}));
			_wait = Registry.RegisterMethod(new("wait", "", new[] { new CommandParameter("frames", typeof(int)) }),
				a => Processor.WaitFrames(a.Get<int>(0)));
		}

		public void Dispose()
		{
			_wait.Dispose();
			_add.Dispose();
		}
	}
}
