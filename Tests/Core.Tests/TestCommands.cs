using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Varcom.Contracts;

namespace Varcom.Tests;

internal static class TestCommands
{
	internal static CommandInfo Info() => new("add", "Add a value", new CommandParameter[]
	{
		new("value", typeof(int)), new("count", typeof(int), true, 1)
	});

	internal static List<CommandResult> Collect(CommandProcessor processor)
	{
		var results = new List<CommandResult>();
		processor.Update(results.Add);
		return results;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static WeakReference RegisterAndDispose(CommandRegistry registry, CommandProcessor processor)
	{
		var owner = new Owner();
		var r = registry.RegisterVariable(new("owned", "", typeof(int), false), () => owner.Value,
			value =>
			{
				owner.Value = value;
				return CommandResult.Ok();
			});
		processor.Enqueue("owned 2");
		r.Dispose();
		return new WeakReference(owner);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static WeakReference RegisterTypedAndDispose(CommandRegistry registry, CommandProcessor processor)
	{
		var handler = new Handler(_ => CommandResult.Ok());
		var registration = registry.Register(Info(), a => new Add((int)a[0]!), handler);
		processor.Enqueue(new Add(1));
		registration.Dispose();
		return new WeakReference(handler);
	}

	internal readonly record struct Add(int Value = 0, int Count = 1) : ICommand;

	internal sealed class Handler(Func<Add, CommandResult> action) : ICommandHandler<Add>
	{
		public CommandResult Handle(in Add command) => action(command);
	}

	internal enum Mode
	{
		One,
		Two
	}

	internal sealed class Owner
	{
		public int Value { get; set; }
	}

	internal readonly record struct WideCommand(long A, long B, long C, long D, long E, long F, long G, long H, string Label) : ICommand;

	internal sealed class WideHandler : ICommandHandler<WideCommand>
	{
		public int Count { get; private set; }

		public CommandResult Handle(in WideCommand command)
		{
			if (command.A + command.B + command.C + command.D + command.E + command.F + command.G + command.H != 36 ||
			    command.Label != "stable")
				return CommandResult.Fail("Payload corrupted");
			Count++;
			return CommandResult.Ok();
		}
	}
}
