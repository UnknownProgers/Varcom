using System;
using Sample;
using Varcom;
using Varcom.Contracts;
using Varcom.Generated.Assembly_Sample;

var registry = new CommandRegistry();
// Optional: load declarations before constructing game objects. Omit this call if not needed.
registry.GenerateMetadata();
var inventory = new Inventory();
var settings = new MovementSettings();
var processor = new CommandProcessor(registry);
var waits = new WaitCommands(processor);
using var commands = new GameCommands(registry, new GiveItemHandler(inventory), settings, waits);
processor.Enqueue("give medkit 2");
processor.Enqueue("sv_gravity 600");
processor.Enqueue("reset_gravity");
processor.Enqueue("sv_gravity");
if (inventory.Count != 0 || settings.Gravity != 800) throw new Exception("Submission executed user code");
processor.Update(result => Console.WriteLine(result.IsSuccess ? result.Value.ToString() : result.Error.Message));
if (inventory.Count != 2 || settings.Gravity != 800) throw new Exception("Generated bindings did not execute");

processor.Enqueue("wait_frames 2; give medkit 1");
processor.Update();
processor.Update();
if (inventory.Count != 2) throw new Exception("Wait resumed before its target update");
processor.Update();
if (inventory.Count != 3) throw new Exception("Wait did not resume at its target update");

processor.Enqueue("wait_ready; give medkit 1");
processor.Update();
processor.Update();
if (inventory.Count != 3) throw new Exception("External suspension resumed without a signal");
// Application readiness event, delivered on the same game thread:
processor.Resume(waits.Pending);
processor.Update();
if (inventory.Count != 4) throw new Exception("External suspension did not resume");


namespace Sample
{
	[CmdModule]
	public sealed partial class GameCommands
	{
	}

	[Cmd("give", "Give an item")]
	public readonly record struct GiveItemCmd(CmdString ItemId, int Count = 1) : ICommand;

	public sealed class WaitCommands(CommandProcessor processor)
	{
		public CommandSuspension Pending { get; private set; }

		[Cmd("wait_ready", "Pause until the application signals readiness")]
		public CommandResult WaitReady()
		{
			var result = processor.Suspend(out var pending);
			if (result.IsSuccess) Pending = pending;
			return result;
		}

		[Cmd("wait_frames", "Pause command execution for processor frames")]
		public CommandResult Wait(int frames) => processor.WaitFrames(frames);
	}

	public sealed class Inventory
	{
		public int Count { get; set; }
	}

	[CmdHandler(typeof(GiveItemCmd))]
	public sealed class GiveItemHandler(Inventory inventory) : ICommandHandler<GiveItemCmd>
	{
		public CommandResult Handle(in GiveItemCmd command)
		{
			if (command.Count <= 0) return CommandResult.Fail("Count must be positive.");
			inventory.Count += command.Count;
			return CommandResult.Ok($"Given {command.Count} x {command.ItemId}");
		}
	}

	public sealed class MovementSettings
	{
		[Var("sv_gravity", "Gravity")] public float Gravity { get; set; } = 800;
		[Var("movement_profile", "Profile")] public CmdString Profile => "default";

		[Cmd("reset_gravity", "Restore default gravity")]
		public CommandResult ResetGravity()
		{
			Gravity = 800;
			return CommandResult.Ok();
		}
	}
}
