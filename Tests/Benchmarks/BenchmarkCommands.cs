using Varcom.Contracts;

namespace Varcom.Benchmarks;

[Cmd("small")]
public readonly record struct SmallCommand(int Value) : ICommand;
[CmdHandler(typeof(SmallCommand))]
public sealed class SmallHandler : ICommandHandler<SmallCommand>
{
	public long Sum { get; private set; }

	public CommandResult Handle(in SmallCommand command)
	{
		Sum += command.Value;
		return CommandResult.Ok();
	}
}
[Cmd("large")]
public readonly record struct LargeCommand(int A, int B, int C, int D, int E, int F, int G, int H, CmdString Label) : ICommand;
[CmdHandler(typeof(LargeCommand))]
public sealed class LargeHandler : ICommandHandler<LargeCommand>
{
	public long Sum { get; private set; }

	public CommandResult Handle(in LargeCommand command)
	{
		Sum += command.A;
		return CommandResult.Ok();
	}
}
public sealed class BenchmarkOwner
{
	[Var("number")] public int Number { get; set; } = 12345;
	public long Calls { get; private set; }
	public CmdString LastText { get; private set; }

	[Cmd("ping")]
	public void Ping() => Calls++;

	[Cmd("echo")]
	public void Echo(CmdString text)
	{
		LastText = text;
		Calls += text.Length;
	}

	[Cmd("many")]
	public void Many(int a = 1, int b = 2, int c = 3, int d = 4, int e = 5, int f = 6, int g = 7, int h = 8) =>
		Calls += a + b + c + d + e + f + g + h;
}
[CmdModule]
public sealed partial class BenchmarkCommandsModule
{
}
