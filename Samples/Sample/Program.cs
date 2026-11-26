using System;
using Sample;
using Varcom;
using Varcom.Contracts;
using Varcom.Generated.Assembly_Sample;

var registry = new CommandRegistry();
var inventory = new Inventory();
var settings = new MovementSettings();
using var commands = registry.Register(new GiveItemHandler(inventory));
using var owner = registry.Register(settings);
var processor = new CommandProcessor(registry);
processor.Enqueue("give medkit 2");
processor.Enqueue("sv_gravity 600");
processor.Enqueue("reset_gravity");
processor.Enqueue("sv_gravity");
if (inventory.Count != 0 || settings.Gravity != 800) throw new Exception("Submission executed user code");
processor.Update(result => Console.WriteLine(result.IsSuccess ? result.Value.ToString() : result.Error.Message));
if (inventory.Count != 2 || settings.Gravity != 800) throw new Exception("Generated bindings did not execute");

namespace Sample
{
	[Cmd("give", "Give an item")]
	public readonly record struct GiveItemCmd(CmdString ItemId, int Count = 1) : ICommand;

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
