using System;
using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class CaughtExceptionTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ThrowingHandler_PreservesExceptionAndContinuesProcessing(bool typed)
	{
		var registry = new CommandRegistry();
		var exception = new InvalidOperationException("Handler failed");
		using var registration = registry.Register(Info(), args => new Add(args.Get<int>(0)),
			new Handler(command => command.Value < 0 ? throw exception : CommandResult.Ok(command.Value)));
		var processor = new CommandProcessor(registry);
		if (typed)
		{
			processor.Enqueue(new Add(-1));
			processor.Enqueue(new Add(2));
		}
		else
			processor.Enqueue("add -1; add 2");

		var results = Collect(processor);

		Assert.Equal(2, results.Count);
		AssertException(results[0], exception);
		Assert.NotNull(exception.StackTrace);
		Assert.True(results[1].IsSuccess);
		Assert.Equal(2, results[1].Value.Get<int>());
	}

	[Fact]
	public void ThrowingFactory_PreservesException()
	{
		var registry = new CommandRegistry();
		var exception = new InvalidOperationException("Factory failed");
		using var registration = registry.Register<Add>(Info(), _ => throw exception, new Handler(_ => CommandResult.Ok()));
		var processor = new CommandProcessor(registry);

		processor.Enqueue("add 1");

		AssertException(Assert.Single(Collect(processor)), exception);
	}

	[Fact]
	public void ThrowingMethod_PreservesException()
	{
		var registry = new CommandRegistry();
		var exception = new InvalidOperationException("Method failed");
		using var registration =
			registry.RegisterMethod(new CommandInfo("broken", "", Array.Empty<CommandParameter>()), _ => throw exception);
		var processor = new CommandProcessor(registry);

		processor.Enqueue("broken");

		AssertException(Assert.Single(Collect(processor)), exception);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ThrowingVariableGetter_PreservesException(bool queued)
	{
		var registry = new CommandRegistry();
		var exception = new InvalidOperationException("Getter failed");
		using var registration = registry.RegisterVariable<int>(new VariableInfo("broken", "", typeof(int), true), () => throw exception);
		CommandResult result;
		if (queued)
		{
			var processor = new CommandProcessor(registry);
			processor.Enqueue("broken");
			result = Assert.Single(Collect(processor));
		}
		else
			result = registry.ReadVariable("broken");

		AssertException(result, exception);
		Assert.NotNull(exception.StackTrace);
	}

	[Fact]
	public void ThrowingVariableSetter_PreservesException()
	{
		var registry = new CommandRegistry();
		var exception = new InvalidOperationException("Setter failed");
		using var registration =
			registry.RegisterVariable(new VariableInfo("broken", "", typeof(int), false), () => 1, _ => throw exception);
		var processor = new CommandProcessor(registry);

		processor.Enqueue("broken 2");

		AssertException(Assert.Single(Collect(processor)), exception);
	}

	[Fact]
	public void OrdinaryFailuresAndSuccess_DoNotHaveExceptions()
	{
		var registry = new CommandRegistry();
		using var registration = registry.Register(Info(), args => new Add(args.Get<int>(0)),
			new Handler(_ => CommandResult.Fail("Inventory full")));
		var processor = new CommandProcessor(registry);
		processor.Enqueue("add 1; add invalid; unknown");

		var results = Collect(processor);

		Assert.Equal(CommandErrorCode.ExecutionFailed, results[0].Error.Code);
		Assert.Equal("Inventory full", results[0].Error.Message);
		Assert.Equal(CommandErrorCode.InvalidArguments, results[1].Error.Code);
		Assert.Equal(CommandErrorCode.UnknownCommand, results[2].Error.Code);
		Assert.All(results, result => Assert.Null(result.Error.Exception));
		Assert.Null(CommandResult.Ok("success").Error.Exception);
	}

	[Fact]
	public void FromException_RejectsNullAndPreservesOriginalException()
	{
		Assert.Throws<ArgumentNullException>(() => CommandResult.FromException(null!));
		var exception = new InvalidOperationException("Explicit exception result");

		AssertException(CommandResult.FromException(exception), exception);
	}

	private static void AssertException(CommandResult result, Exception exception)
	{
		Assert.False(result.IsSuccess);
		Assert.Equal(CommandErrorCode.ExceptionThrown, result.Error.Code);
		Assert.Equal(exception.Message, result.Error.Message);
		Assert.Same(exception, result.Error.Exception);
	}
}
