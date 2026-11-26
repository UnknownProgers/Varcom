using System;
using BenchmarkDotNet.Attributes;
using Varcom.Contracts;
using Varcom.Generated.Assembly_Benchmarks;

namespace Varcom.Benchmarks;

[MemoryDiagnoser]
public class OperationBenchmarks
{
	private CommandRegistry _registry = null!;
	private CommandProcessor _processor = null!;
	private BenchmarkOwner _owner = null!;
	private IDisposable _registration = null!;
	private Action<CommandResult> _onResult = null!;
	private CommandValue _value;

	[GlobalSetup]
	public void Setup()
	{
		_registry = new CommandRegistry();
		_owner = new BenchmarkOwner();
		_registration = _registry.Register(_owner);
		_processor = new CommandProcessor(_registry);
		_onResult = result => _value = result.Value;
		Method();
		VariableWrite();
		VariableRead();
		EscapedText();
		EightExplicitArguments();
		EightDefaultArguments();
		if (_owner.Calls != 86 || !_owner.LastText.Equals("hello \"world\""))
			throw new InvalidOperationException("Benchmark owner did not execute the expected work.");
	}

	[Benchmark]
	public void EmptyUpdate() => _processor.Update();

	[Benchmark]
	public long Method()
	{
		_processor.Enqueue("ping");
		_processor.Update();
		return _owner.Calls;
	}

	[Benchmark]
	public int VariableWrite()
	{
		_processor.Enqueue("number 12345");
		_processor.Update();
		return _owner.Number;
	}

	[Benchmark]
	public int VariableRead()
	{
		_processor.Enqueue("number");
		_processor.Update(_onResult);
		return _value.Get<int>();
	}

	[Benchmark]
	public CommandResult ImmediateVariableRead() => _registry.ReadVariable("number");

	[Benchmark]
	public long EscapedText()
	{
		_processor.Enqueue("echo \"hello \\\"world\\\"\"");
		_processor.Update();
		return _owner.Calls;
	}

	[Benchmark]
	public long EightExplicitArguments()
	{
		_processor.Enqueue("many 1 2 3 4 5 6 7 8");
		_processor.Update();
		return _owner.Calls;
	}

	[Benchmark]
	public long EightDefaultArguments()
	{
		_processor.Enqueue("many");
		_processor.Update();
		return _owner.Calls;
	}

	[GlobalCleanup]
	public void Cleanup() => _registration.Dispose();
}
