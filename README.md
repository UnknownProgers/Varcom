# Varcom

Current version: **0.3.0-alpha.1**.

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

## The main concepts

Think of `give medkit 2`: the command says **what to do**, the handler knows **how to
do it**, and registration makes that handler **available to the processor**.

| Concept          | Responsibility                                                                                                          | Example                                                                                         |
|------------------|-------------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------|
| Command          | Carries the data for one action. It does not execute itself.                                                            | `GiveItemCmd` contains the item ID and count.                                                   |
| Handler          | Contains the code that performs the action, using its existing game services.                                           | `GiveItemHandler.Handle` changes the inventory.                                                 |
| Command method   | A shorter alternative to a command struct plus a handler.                                                               | A method marked `[Cmd("ping")]` is called on its registered object.                             |
| Console variable | Exposes a field or property for reading and, when writable, changing through commands.                                  | `[Var("capacity")]` exposes inventory capacity.                                                 |
| Registry         | Keeps track of the commands and variables currently available.                                                          | Connects the name `give` and the type `GiveItemCmd` to their handler.                           |
| Registration     | A live connection between a command or variable and a particular object. Removing it makes that connection unavailable. | Registers `give` against this level's `GiveItemHandler` instance.                               |
| Module           | Registers a group together and removes that group on `Dispose()`.                                                       | `LevelCommands` groups the commands available while a level is loaded.                          |
| Processor        | Accepts calls, queues them and executes them when the game calls `Update()`.                                            | `Enqueue("give medkit 2")`, then `Update()`.                                                    |
| Result           | Reports whether accepting or executing a call succeeded, with an optional value or error.                               | An enqueue success means queued; an execution success means the handler completed successfully. |
| Source generator | Writes the connection code at build time from your attributes.                                                          | Generates registration methods and module constructors.                                         |

### Handler versus registration

A handler is an ordinary object with game logic. A registration is the connection that
allows Varcom to call that object. Creating a handler alone does not make its commands
available; attributes describe the connection, and generated registration code establishes it.

The lower-level, per-object form looks like this (import the generated namespace
`Varcom.Generated.Assembly_MyGame` for a project named MyGame):

```csharp
var handler = new GiveItemHandler(inventory);
var registration = registry.Register(handler);

processor.Enqueue("give medkit 2");
processor.Update(); // Calls this handler.

registration.Dispose(); // Removes its commands; does not dispose the handler.
```

Declare at least one module with `[CmdModule]` in a project containing attributed handlers
or objects with commands/variables. The generator does not create an implicit module for you.
A module registers several objects together, so you do not have to keep individual
registration handles. The direct Register overloads above are lower-level generated APIs;
modules are the normal entry point.

### Who owns the lifetime?

Your application creates handlers and other game objects. Varcom uses those existing
instances and keeps them reachable while their registrations are active. It never calls
`Dispose()` on those objects. The application also decides when registrations are removed,
either through individual handles or through a module.

For example, on level load the application creates the inventory, its handler and a
`LevelCommands` module. On level unload it disposes that module first, making the level's
commands unavailable, and then releases the level's own resources. A separate global module
can remain active for the whole game. "Level" and "global" are your grouping choices;
Varcom has no special rules for either.

A handler may outlive its registration and be registered again later. Conversely, leaving
an old registration active keeps its handler reachable and its commands available, even if
the game has otherwise finished with that level. Removing a registration does not undo
completed actions. Calls already queued against it fail as unavailable when processed;
they are not redirected to a newly registered handler with the same command name.

The processor does not run the game loop or own the game simulation. Your game calls
`Update()` where command execution belongs. Calls are processed in queue order; calls added
during an update wait for the next update. Use the registry, processor and registration
handles on the same game thread.

## Quick start

Reference the Varcom package. It includes the runtime and the source generator.
Declare a partial class with `[CmdModule]` to group your commands.

```csharp
using System;
using Varcom;
using Varcom.Contracts;

var registry = new CommandRegistry();
var inventory = new Inventory();
using var commands = new GameCommands(registry, new GiveItemHandler(inventory), inventory);
var processor = new CommandProcessor(registry);

processor.Enqueue("give medkit 2");
processor.Enqueue("capacity 20");
processor.Update(result =>
    Console.WriteLine(result.IsSuccess ? result.Value.ToString() : result.Error.Message));

[CmdModule]
public sealed partial class GameCommands { }

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

`[CmdModule]` without a type list selects all valid attributed handlers and objects declared
in the current assembly. The generator creates the module constructor and `Dispose()`.
The constructor takes CommandRegistry first, then an existing instance per object, sorted by
fully qualified type name. Construction registers the group atomically; Dispose removes
registrations without disposing those objects. No implicit module is generated.

With DI, the container can provide the constructor dependencies when it creates the module;
without DI, pass the existing objects as above. The module must actually be created to
activate its commands, and disposed to disconnect them.

To choose independent groups, declare modules with explicit type lists:

```csharp
// In the host, using existing objects:
using var actions = new InventoryCommands(registry, new GiveItemHandler(inventory));
using var variables = new InventoryVariables(registry, inventory);

[CmdModule(typeof(GiveItemHandler))]
public sealed partial class InventoryCommands { }

[CmdModule(typeof(Inventory))]
public sealed partial class InventoryVariables { }
```

Construction belongs in host initialization code; module declarations are top-level classes. In explicit lists constructor dependencies
follow the declared order. `[CmdModule]`
with no types selects all local attributed owners. Modules are non-generic top-level
partial classes without a base class; their constructor and Dispose are generated.

Explicit lists may reference public owners from other assemblies when those assemblies
provide the generated Register overload. Automatic selection is local, not an assembly scan.
A module may not itself declare commands/variables or act as a command handler.

Without DI the host creates the instances and module as above. With DI the host registers
its services and module with its own container; the generated constructor exposes the exact
dependencies for constructor injection. Varcom does not register services, resolve them,
create owners or depend on lifecycle interfaces. The host decides when to activate/dispose
modules. There are no built-in application/level categories, and modules must not be
constructed over already-active registrations for the same commands.

For `+`/`-` bindings declare explicit command names; no automatic pairing is introduced.

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

## Declarations and available commands

The registry has two independent catalogs:

| Method          | What it returns                                                                              |
|-----------------|----------------------------------------------------------------------------------------------|
| `GetMetadata()` | Declarations explicitly loaded by `GenerateMetadata()`, including commands without handlers. |
| `GetEntries()`  | Commands and variables registered and available right now.                                   |

Both start empty. Metadata is optional: if you never call `GenerateMetadata()`, its catalog
stays empty and no storage or declaration entries are created for it. Creating modules does
not generate metadata. There is no constructor flag and no static metadata store.

The Varcom generator writes the `GenerateMetadata()` extension in your project from its
attributes. You only call it; you do not describe or register the declarations by hand.
For a project named MyGame, import `Varcom.Generated.Assembly_MyGame` (punctuation in assembly
names becomes underscores):

```csharp
using Varcom.Generated.Assembly_MyGame;

var registry = new CommandRegistry();
registry.GenerateMetadata(); // Optional. No handlers or variable owners need to exist yet.

foreach (var entry in registry.GetMetadata())
{
    var available = registry.IsAvailable(entry.Info.Name);
    Console.WriteLine($"{entry.Info.Name}: {entry.Info.Description}, available: {available}");
}
```

This creates names, descriptions and parameter information in this registry. Variables also
describe their type and whether they are read-only. It does not create game objects, call
getters, read variable values or make commands executable.

Later, creating a module connects existing game objects and fills the active catalog.
Disposing that module removes its active registrations, leaving the metadata unchanged.
Metadata can also be generated after modules have been created or disposed.

Repeated `GenerateMetadata()` calls skip declarations already present before creating their
entries. Metadata and active-entry snapshots are read-only and cached until their respective
catalog changes. Each registry owns its own metadata; generated code holds no static copies.
If you retain a returned snapshot, that snapshot keeps its entries alive like any other list.

`IsAvailable(name)` checks active names without regard to case. Distinct declarations with
the same name remain separate metadata entries; availability by name means that some
registration under that name is active, not necessarily a particular owner. Multiple handlers
for one command struct share one declaration.

Each generated method covers its assembly's attributed declarations, plus commands referenced
by its local handlers. It does not scan other assemblies. To include several assemblies, call
their generated methods explicitly; this also avoids ambiguous extensions when multiple generated
namespaces are imported:

```csharp
Varcom.Generated.Assembly_Gameplay.CommandMetadata.GenerateMetadata(registry);
Varcom.Generated.Assembly_DebugTools.CommandMetadata.GenerateMetadata(registry);
```

Declarations are deduplicated by their runtime Type and a generated numeric member index,
not their console name. Type identity already distinguishes assemblies; no string IDs are stored. Explicit module lists do not automatically
import external metadata.

The first generation and first snapshot read allocate during setup. Without generation,
`GetMetadata()` returns a shared empty list. Names and argument information needed to parse
and execute commands still exist in active bindings; normal registration and buffer growth
still allocate. Generated active registrations omit help descriptions. The warmed execution
paths remain allocation-free whether or not you generate metadata.

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
