using System;
using System.Runtime.CompilerServices;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Varcom.Generated.Assembly_Benchmarks;

namespace Varcom.Benchmarks;

[MemoryDiagnoser]
public class PipelineBenchmarks
{
	private CommandProcessor _processor = null!;
	private SmallHandler _small = null!;
	private LargeHandler _large = null!;
	private Payload128Handler _payload128 = null!;
	private IDisposable _smallRegistration = null!;
	private IDisposable _largeRegistration = null!;
	private IDisposable _payload128Registration = null!;
	private string _sequence = null!;
	private readonly SmallCommand _smallCommand = new(1);
	private readonly LargeCommand _largeCommand = new(1, 2, 3, 4, 5, 6, 7, 8, "stable");

	private readonly Payload128Command _payload128Command = new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,
		22, 23, 24, 25, 26, "stable");

	[Params(1, 32, 128, 256)] public int BatchSize { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		var registry = new CommandRegistry();
		_small = new SmallHandler();
		_large = new LargeHandler();
		_payload128 = new Payload128Handler();
		_smallRegistration = registry.Register(_small);
		_largeRegistration = registry.Register(_large);
		_payload128Registration = registry.Register(_payload128);
		_processor = new CommandProcessor(registry);
		_sequence = string.Join("; ", Enumerable.Repeat("small 1", BatchSize));
		TypedSmall();
		TypedLarge();
		Typed128();
		TextIndividual();
		TextSequence();
		if (_small.Sum != 3L * BatchSize || _large.Sum != BatchSize || _payload128.Sum != BatchSize ||
		    Unsafe.SizeOf<Payload128Command>() != 128)
			throw new InvalidOperationException("Benchmark commands did not execute the expected work.");
	}

	[Benchmark(Baseline = true)]
	public long TypedSmall()
	{
		for (var i = 0; i < BatchSize; i++) _processor.Enqueue(in _smallCommand);
		_processor.Update();
		return _small.Sum;
	}

	[Benchmark]
	public long TypedLarge()
	{
		for (var i = 0; i < BatchSize; i++) _processor.Enqueue(in _largeCommand);
		_processor.Update();
		return _large.Sum;
	}

	[Benchmark]
	public long Typed128()
	{
		for (var i = 0; i < BatchSize; i++) _processor.Enqueue(in _payload128Command);
		_processor.Update();
		return _payload128.Sum;
	}

	[Benchmark]
	public long TextIndividual()
	{
		for (var i = 0; i < BatchSize; i++) _processor.Enqueue("small 1");
		_processor.Update();
		return _small.Sum;
	}

	[Benchmark]
	public long TextSequence()
	{
		_processor.Enqueue(_sequence);
		_processor.Update();
		return _small.Sum;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_smallRegistration.Dispose();
		_largeRegistration.Dispose();
		_payload128Registration.Dispose();
	}
}
