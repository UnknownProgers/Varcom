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
	public void AdditionsDuringWaitPreserveInsertionAndEnqueueOrder()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 3; add 3");
		fixture.Processor.Update();
		fixture.Processor.Enqueue(new Add(4));
		fixture.Processor.Insert("add 1");
		fixture.Processor.Insert("add 2");
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2, 3, 4 }, fixture.Seen);
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
		fixture.Processor.Update();
		Assert.Equal(new[] { 2, 3 }, fixture.Seen);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(3)]
	public void FrameWaitCanBeStartedOutsideUpdate(int frames)
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1");
		Assert.True(fixture.Processor.WaitFrames(frames).IsSuccess);
		for (var i = 1; i < frames; i++)
		{
			fixture.Processor.Update();
			Assert.Empty(fixture.Seen);
		}

		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void ExternalSignalResumesOnNextUpdateAndIgnoresStaleOrForeignTokens()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 2");
		var old = Suspend(fixture.Processor);
		Assert.True(fixture.Processor.Resume(old));
		var active = Suspend(fixture.Processor);
		fixture.Processor.Insert("add 1");
		Assert.False(fixture.Processor.Resume(old));
		Assert.False(fixture.Processor.Resume(default));
		var other = new CommandProcessor(fixture.Registry);
		Assert.False(other.Resume(active));
		Assert.False(fixture.Processor.Resume(Suspend(other)));
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		Assert.True(fixture.Processor.Resume(active));
		Assert.False(fixture.Processor.Resume(active));
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1, 2 }, fixture.Seen);
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
	public void ExternalSuspensionFailsWithoutChangingRemainingFrames()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("wait 3; add 1");
		fixture.Processor.Update();
		Assert.False(fixture.Processor.Suspend(out var rejected).IsSuccess);
		Assert.False(fixture.Processor.Resume(rejected));
		fixture.Processor.Update();
		fixture.Processor.Update();
		Assert.Empty(fixture.Seen);
		fixture.Processor.Update();
		Assert.Equal(new[] { 1 }, fixture.Seen);
	}

	[Fact]
	public void SecondExternalSuspensionFailsAndOriginalTokenRemainsActive()
	{
		using var fixture = new Fixture();
		fixture.Processor.Enqueue("add 1");
		var original = Suspend(fixture.Processor);
		Assert.Equal(CommandErrorCode.ExecutionFailed, fixture.Processor.Suspend(out var rejected).Error.Code);
		Assert.False(fixture.Processor.Resume(rejected));
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
