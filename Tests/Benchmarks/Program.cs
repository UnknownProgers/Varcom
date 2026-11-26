using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Running;
using Varcom.Benchmarks;

if (args.Length == 1 && args[0] == "--phases")
{
	ArgumentPhaseMeasurement.Run();
	return;
}

if (args.Length == 1 && args[0] == "--verify")
{
	foreach (var size in new[] { 1, 32, 128, 256 })
	{
		var benchmark = new PipelineBenchmarks { BatchSize = size };
		benchmark.Setup();
		benchmark.Cleanup();
	}

	var operations = new OperationBenchmarks();
	operations.Setup();
	operations.EmptyUpdate();
	if (!operations.ImmediateVariableRead().IsSuccess) throw new InvalidOperationException("Immediate read failed.");
	operations.Cleanup();
	foreach (var count in new[] { 1, 128, 1024 })
	{
		var benchmark = new RegistryBenchmarks { RegisteredCommands = count };
		benchmark.Setup();
		benchmark.Cleanup();
	}

	Console.WriteLine(
		$"All benchmark setups executed the expected work. Payload sizes: {Unsafe.SizeOf<SmallCommand>()} / {Unsafe.SizeOf<LargeCommand>()} / {Unsafe.SizeOf<Payload128Command>()} bytes.");
	return;
}

BenchmarkSwitcher.FromAssembly(typeof(PipelineBenchmarks).Assembly).Run(args);
