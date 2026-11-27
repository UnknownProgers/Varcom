using Varcom.Contracts;
using Xunit;
using static Varcom.Tests.TestCommands;

namespace Varcom.Tests;

public sealed class VariableTests
{
	[Fact]
	public void Variable_ReadsAndWritesAreDeferredAndInvalidWritesPreserveValue()
	{
		var registry = new CommandRegistry();
		var gravity = 800f;
		using var binding = registry.RegisterVariable(new VariableInfo("gravity", "Gravity", typeof(float), false),
			() => gravity, v =>
			{
				gravity = v;
				return CommandResult.Ok();
			});
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("gravity abc").IsSuccess);
		Assert.True(processor.Enqueue("gravity NaN").IsSuccess);
		Assert.All(Collect(processor), r => Assert.Equal(CommandErrorCode.InvalidArguments, r.Error.Code));
		Assert.Equal(800, gravity);
		processor.Enqueue("gravity 600");
		processor.Enqueue("gravity");
		Assert.Equal(800, gravity);
		var done = Collect(processor);
		Assert.Equal(600, gravity);
		Assert.True(done[1].IsSuccess);
		Assert.Equal("600", done[1].Value.ToString());
		Assert.True(registry.ReadVariable("gravity").IsSuccess);
		Assert.Equal(600f, registry.ReadVariable("gravity").Value.Get<float>());
		using var ro = registry.RegisterVariable(new VariableInfo("profile", "Profile", typeof(CmdString), true),
			() => new CmdString("default"));
		Assert.True(processor.Enqueue("profile other").IsSuccess);
		Assert.Equal(CommandErrorCode.ReadOnly, Collect(processor)[0].Error.Code);
	}

	[Fact]
	public void Variable_PropagatesSetterFailureWithoutChangingValue()
	{
		var registry = new CommandRegistry();
		var capacity = 10;
		using var registration = registry.RegisterVariable(new("capacity", "", typeof(int), false), () => capacity,
			v =>
			{
				if (v < 0) return CommandResult.Fail("Must be nonnegative");
				capacity = v;
				return CommandResult.Ok();
			});
		var processor = new CommandProcessor(registry);
		processor.Enqueue("capacity -1");
		Assert.False(Collect(processor)[0].IsSuccess);
		Assert.Equal(10, capacity);
	}

	[Fact]
	public void Variable_ParsesBooleanAndEnumAndRejectsInvalidValues()
	{
		var registry = new CommandRegistry();
		var enabled = true;
		var mode = Mode.One;
		using var b = registry.RegisterVariable(new("enabled", "", typeof(bool), false), () => enabled, v =>
		{
			enabled = v;
			return CommandResult.Ok();
		});
		using var e = registry.RegisterVariable(new("mode", "", typeof(Mode), false), () => mode, v =>
		{
			mode = v;
			return CommandResult.Ok();
		});
		var processor = new CommandProcessor(registry);
		Assert.True(processor.Enqueue("enabled garbage").IsSuccess);
		Assert.True(processor.Enqueue("mode 99").IsSuccess);
		Assert.All(Collect(processor), r => Assert.Equal(CommandErrorCode.InvalidArguments, r.Error.Code));
		processor.Enqueue("enabled 0");
		processor.Enqueue("mode two");
		processor.Update();
		Assert.False(enabled);
		Assert.Equal(Mode.Two, mode);
	}

	[Fact]
	public void Variable_DiscoveryAndParsingDoNotInvokeGetter()
	{
		var registry = new CommandRegistry();
		var processor = new CommandProcessor(registry);
		var calls = 0;
		using var registration = registry.RegisterVariable(new VariableInfo("profile", "", typeof(CmdString), true),
			() =>
			{
				calls++;
				return new CmdString("default");
			});
		registry.GetEntries();
		Assert.Equal(0, calls);
		Assert.True(processor.Enqueue("profile changed").IsSuccess);
		Assert.Equal(CommandErrorCode.ReadOnly, Collect(processor)[0].Error.Code);
		Assert.True(processor.Enqueue("profile").IsSuccess);
		Assert.Equal(0, calls);
		var result = Collect(processor)[0];
		Assert.True(result.IsSuccess);
		Assert.Equal("default", result.Value.ToString());
		Assert.Equal(1, calls);
	}
}
