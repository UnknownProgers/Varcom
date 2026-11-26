using System;
using System.Diagnostics;
using System.Globalization;
using Varcom.Generated.Assembly_Benchmarks;

namespace Varcom.Benchmarks;

// Diagnostic split only. Timestamp overhead is amortized over a batch, not subtracted.
// End-to-end optimization claims are checked independently with BenchmarkDotNet.
internal static class ArgumentPhaseMeasurement
{
	internal static void Run()
	{
		Console.WriteLine("Input,BatchSize,EnqueueMedianNsPerCommand,UpdateMedianNsPerCommand,AllocatedBytes");
		Measure("defaults", "many");
		Measure("explicit8", "many 1 2 3 4 5 6 7 8");
	}

	private static void Measure(string name, string text)
	{
		const int batch = 128;
		const int warmup = 2000;
		const int iterations = 10000;
		const int samples = 5;
		var owner = new BenchmarkOwner();
		var registry = new CommandRegistry();
		using var registration = registry.Register(owner);
		var processor = new CommandProcessor(registry, batch);
		var enqueueTimes = new double[samples];
		var updateTimes = new double[samples];
		for (var round = 0; round < warmup; round++)
		{
			for (var i = 0; i < batch; i++) processor.Enqueue(text);
			processor.Update();
		}

		long allocated = 0;
		for (var sample = 0; sample < samples; sample++)
		{
			long enqueueTicks = 0, updateTicks = 0;
			var before = GC.GetAllocatedBytesForCurrentThread();
			for (var round = 0; round < iterations; round++)
			{
				var start = Stopwatch.GetTimestamp();
				for (var i = 0; i < batch; i++) processor.Enqueue(text);
				var middle = Stopwatch.GetTimestamp();
				processor.Update();
				var end = Stopwatch.GetTimestamp();
				enqueueTicks += middle - start;
				updateTicks += end - middle;
			}

			allocated += GC.GetAllocatedBytesForCurrentThread() - before;
			var scale = 1_000_000_000d / Stopwatch.Frequency / iterations / batch;
			enqueueTimes[sample] = enqueueTicks * scale;
			updateTimes[sample] = updateTicks * scale;
		}

		if (owner.Calls != (warmup + iterations * samples) * (long)batch * 36)
			throw new InvalidOperationException("Phase measurement did not execute the expected work.");
		Array.Sort(enqueueTimes);
		Array.Sort(updateTimes);
		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"{name},{batch},{enqueueTimes[samples / 2]:F2},{updateTimes[samples / 2]:F2},{allocated}"));
	}
}
