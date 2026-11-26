# Varcom

Current version: **0.1.0-alpha.3**.

A C# command and console variable framework for games, featuring attribute-based
declarations, source generation, typed handlers, and queued execution.

Varcom has no engine, GUI, logging or DI dependency. The host constructs its services,
registers their existing instances, and calls `CommandProcessor.Update()` on its game thread.
Successful typed and text `Enqueue` + `Update` paths allocate zero bytes after warmup,
within the allocated queue/scratch capacity, provided handlers and result callbacks also
avoid allocations. Expected parsing errors use value results and prebuilt diagnostics.
`new CommandProcessor(registry, initialCapacity: 128)` sets the initial execution, text
staging and per-type queue capacity. Buffers grow normally when required; growth allocates.
Registration, first-use runtime caches and user-created input strings are outside this guarantee.

Text parameters use `CmdString`, not `string`. It retains an immutable view into the
submitted string, including quote/escape information, and remains valid after execution.
Use `Equals`, enumeration or `TryCopyTo(Span<char>)` without allocations. `ToString()` explicitly
materializes decoded text and may allocate. Retaining a small view retains its entire source.

`CommandResult.Value` is a `CommandValue`: use `Get<int>()`, `Get<CmdString>()` or
`TryFormat(Span<char>, out int)` without boxing or allocating formatted strings. Supported
values are CmdString, bool, int, float, double and enum. `CommandValue.From(enumValue)`
creates a typed enum result. `CommandError` is also a value type; check `IsSuccess`, then
`Error.Code`/`Error.Message`. Expected errors carry stable messages and parameter names;
the caller retains the submitted input for display. Constructing dynamic messages or
throwing exceptions can still allocate.

Commands are always structs. Enqueue, registration and handlers enforce `struct, ICommand`;
class commands and interface-typed submissions are rejected at compile time.

There is no mandatory invocation context: a method's owner is its registered instance.
Handlers return a value-type `CommandResult`, including expected business failures.
`Enqueue` returns the same result type: success means accepted, not executed. `Update()`
executes the queue without allocating a results collection. Use `Update(OnResult)` to
receive results inline; keep/reuse the delegate when calling every frame. Without a
callback execution results are discarded, including failures. No tickets or completion objects.
The supplied callback must not throw; a callback exception stops the current batch and
leaves unprocessed calls in the queue.

## Quick start

Reference the Varcom package. It includes the runtime and the source generator.
For a project named `MyGame`, import the generated namespace
`Varcom.Generated.Assembly_MyGame` (punctuation in assembly names becomes underscores).

```csharp
using System;
using Varcom;
using Varcom.Contracts;
using Varcom.Generated.Assembly_MyGame;

var registry = new CommandRegistry();
var inventory = new Inventory();
using var commands = registry.Register(new GiveItemHandler(inventory));
using var settings = registry.Register(inventory);
var processor = new CommandProcessor(registry);

processor.Enqueue("give medkit 2");
processor.Enqueue("capacity 20");
processor.Update(result =>
    Console.WriteLine(result.IsSuccess ? result.Value.ToString() : result.Error.Message));

[Cmd("give", "Give an item")]
public readonly record struct GiveItemCmd(CmdString ItemId, int Count = 1) : ICommand;

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

public sealed class Inventory
{
    public int Count { get; set; }
    [Var("capacity")] public int Capacity { get; set; } = 10;
}
```

Registration has one generated, type-checked overload per attributed owner/handler type.
The application owns those instances and keeps each returned `IDisposable` until its
scope ends. Varcom never creates or disposes game services. For `+`/`-` bindings declare
explicit command names; categories do not change execution behavior.

See the [complete sample](Samples/Sample/Program.cs) and
[execution/API rules](docs/stage-1-api.md).

A single submission can contain a command sequence:

```csharp
processor.Enqueue("give medkit 2; capacity 20");
```

Semicolons inside quotes are preserved. A parsing error rejects the whole sequence before
it enters the queue; existing queued work is untouched. Command construction is deferred
until Update, so constructor exceptions are execution failures. Commands produced by handlers
still wait for the next Update. Execution failures do not roll back prior game actions.

## Development

The repository uses .NET 8/C# 12. The generator targets netstandard2.0 and Roslyn 4.8.
The included .slnx requires a newer IDE/SDK to open; with SDK 8 build the project paths below.

```sh
dotnet build Core/Core.csproj
dotnet test Tests/Core.Tests/Core.Tests.csproj -c Release
dotnet test Tests/Generator.Tests/Generator.Tests.csproj -c Release
dotnet run --project Samples/Sample/Sample.csproj
dotnet pack Core/Core.csproj -c Release -o artifacts
```

Both test projects use xUnit and are discovered by IDE test runners and `dotnet test`.
Core.Tests separates processor, text, registry, variable, lifetime and allocation behavior.
Allocation tests use a nonparallel collection and measure only the warmed operation loop;
Release is recommended for allocation checks. Generator.Tests compiles attributed sources
and checks emitted code and diagnostics using data-driven theories.

Performance benchmarks live in [Tests/Benchmarks](Tests/Benchmarks/README.md). Run the
console project in Release; its results are separate from xUnit correctness/allocation tests.

The initial timing report is in [performance-baseline](docs/performance-baseline.md),
including the requested 128-byte payload comparison and original CSV measurements.

The [argument-validation follow-up](docs/performance-arguments.md) records the first
measured optimization, preserving zero allocations and existing execution semantics.
