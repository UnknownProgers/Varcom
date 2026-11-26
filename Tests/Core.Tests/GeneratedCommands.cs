using Varcom.Contracts;

namespace Varcom.Tests;

public enum GeneratedMode : long
{
	Negative = -1,
	First = 1,
	Second = 2
}
[Cmd("generated")]
public readonly record struct GeneratedCommand(CmdString Text, int Count = 1, GeneratedMode Mode = GeneratedMode.Second) : ICommand;
[CmdHandler(typeof(GeneratedCommand))]
public sealed class GeneratedHandler : ICommandHandler<GeneratedCommand>
{
	public CmdString Text { get; private set; }
	public int Count { get; private set; }
	public GeneratedMode Mode { get; private set; }

	public CommandResult Handle(in GeneratedCommand command)
	{
		Text = command.Text;
		Count += command.Count;
		Mode = command.Mode;
		return CommandResult.Ok(command.Count);
	}
}
public sealed class GeneratedOwner
{
	[Var("readonly")] public int ReadOnly => 7;
	[Var("number")] public int Number { get; set; }
	[Var("label")] public CmdString Label { get; set; }
	[Var("mode")] public GeneratedMode Mode { get; set; }
	[Var("enabled")] public bool Enabled { get; set; }
	[Var("single")] public float Single { get; set; }
	[Var("double")] public double Double { get; set; }
	public int Calls { get; private set; }

	[Cmd("method")]
	public CommandResult Method(CmdString text, bool enabled, float single, double number)
	{
		Label = text;
		Enabled = enabled;
		Single = single;
		Double = number;
		Calls++;
		return CommandResult.Ok(number);
	}
}
