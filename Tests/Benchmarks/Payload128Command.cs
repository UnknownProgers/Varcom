using Varcom.Contracts;

namespace Varcom.Benchmarks;

[Cmd("payload128")]
public readonly record struct Payload128Command(
	int A,
	int B,
	int C,
	int D,
	int E,
	int F,
	int G,
	int H,
	int I,
	int J,
	int K,
	int L,
	int M,
	int N,
	int O,
	int P,
	int Q,
	int R,
	int S,
	int T,
	int U,
	int V,
	int W,
	int X,
	int Y,
	int Z,
	CmdString Label) : ICommand;

[CmdHandler(typeof(Payload128Command))]
public sealed class Payload128Handler : ICommandHandler<Payload128Command>
{
	public long Sum { get; private set; }

	public CommandResult Handle(in Payload128Command command)
	{
		Sum += command.A;
		return CommandResult.Ok();
	}
}
