using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Varcom.Contracts;

namespace Varcom.Benchmarks;

[MemoryDiagnoser]
public class RegistryBenchmarks
{
	private readonly List<IDisposable> _registrations = new();
	private CommandProcessor _processor = null!;
	private long _calls;

	[Params(1, 128, 1024)] public int RegisteredCommands { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		var registry = new CommandRegistry();
		for (var i = 1; i < RegisteredCommands; i++)
			_registrations.Add(registry.RegisterMethod(new($"dummy_{i}", "", Array.Empty<CommandParameter>()), _ => CommandResult.Ok()));
		_registrations.Add(registry.RegisterMethod(new("target", "", Array.Empty<CommandParameter>()), _ =>
		{
			_calls++;
			return CommandResult.Ok();
		}));
		_processor = new CommandProcessor(registry);
		if (LookupAndExecute() != 1) throw new InvalidOperationException("Target command did not execute.");
	}

	[Benchmark]
	public long LookupAndExecute()
	{
		_processor.Enqueue("target");
		_processor.Update();
		return _calls;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		foreach (var registration in _registrations) registration.Dispose();
	}
}
