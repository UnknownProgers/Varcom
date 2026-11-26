# Varcom API and execution rules

Status: implemented initial runtime and generator. This replaces the compile-only API
proposal formerly in ApiSketch. There is no DI resolver, processor interface, module
interface or mandatory CommandContext. SARG integration and NuGet publication are separate work.

## Ownership is not invocation context

The owner is the existing object whose method/property is exposed. `registry.Register(owner)`
generates delegates to that exact instance. Its state and dependencies remain its own.
An explicit handler is also an existing instance, accepting only a typed command and
returning CommandResult. If it needs output, a logger, permissions or game services,
the application passes them through its constructor. No service locator is introduced.

## Contracts

Public contracts live in `Core/Contracts`, one top-level type per file, under
`Varcom.Contracts`. Attributes remain in `Varcom`. Internal definitions, parsing and storage
use `Varcom.Bindings`, `Varcom.Parsing` and `Varcom.Processing`.

- ICommand: marker for typed data; no priority or execution method.
- ICommandHandler<T>.Handle(in T): returns success with an optional result value or failure.
- Cmd/Var/CmdHandler: declarations consumed at build time, not a reflection scan.
- CommandRegistry: concrete runtime registry, direct registration and read-only discovery.
- CommandProcessor: concrete synchronous FIFO queue, text and typed Enqueue, Update.
- CommandInfo/CommandParameter: description and explicit positional parameter metadata.
- VariableInfo: value type and read-only state independent of invocation.
- CommandEntry: snapshot of currently registered metadata. Disposed bindings disappear;
  a persistent catalog of unavailable declarations is not included in this iteration.
- CommandResult: a readonly record struct with IsSuccess, Error and Value; no inheritance.
  Ok() and Fail create value results without a heap allocation for an existing message.
  Error.Code is None for success; Error is no longer a nullable reference.
  Enqueue success means queued; execution success means the handler finished successfully.
  ReadVariable returns the same result with the raw value. Read-only/type metadata is
  already available in VariableInfo; it is not duplicated in the result.
- CommandError: readonly value type with machine-readable code, message and optional argument name.
- CmdString: immutable view of decoded command text backed by the original input.
- CommandValue: supported scalar, enum or text value stored without boxing.
- CommandArguments: generated-binding input; Get<T>(index) reads typed values without object arrays.
- IDisposable registration handles: idempotently remove their own bindings, not services.
- RegistrationGroup: atomically connects multiple members of an owner, rolling back on failure.

## Generated registration

Generated extensions live in `Varcom.Generated.Assembly_<sanitized assembly name>`.
`registry.Register(handler)` adds the handler's command metadata, factory and typed binding.
`registry.Register(owner)` connects all directly declared attributed methods and variables
on that instance. One class may contain both Cmd and Var, and may also be a handler.

Owners are public non-static non-generic top-level classes. Commands are public top-level
non-generic structs (including record structs) implementing ICommand, with one unambiguous
public constructor. Methods are public synchronous non-generic methods returning void or
CommandResult; void methods are adapted to success. Static methods on an instance owner
are supported, but static owner classes are not supported yet. Private members, async,
ref/out/in method arguments, params, generics, inheritance discovery and custom parsers
are not included. Unsupported declarations produce VARCOM001 where analyzed.

No Global/Level enum exists. The host registers ready-made handler/owner instances at the
appropriate time and disposes handles at scope end. There is no need for separate assemblies
for global and level commands. Duplicate active names or command types throw ArgumentException.
Same-name conflicts between owners are checked on registration, not globally at compile time.

## Metadata catalog

CommandRegistry owns two independent catalogs: GetMetadata returns explicitly loaded
command/variable declarations, while GetEntries returns active registrations. Both start
empty. Metadata storage is allocated lazily on the first AddMetadata; registry construction
and module registration never populate it. No flag or static metadata store exists.

The generator emits a GenerateMetadata(this CommandRegistry) extension in the assembly's
generated namespace. Calling it creates metadata without owners or handlers. It includes
local command structs (including those without handlers), command methods, variables and
referenced command structs used by local handlers. Referenced module owners do not import
their assembly's metadata; invoke each assembly's generated extension explicitly.

Generated registration code checks ContainsMetadata before constructing an entry. Identity
uses the runtime Type plus a generated numeric member index (zero for command structs), preserving overloads
and distinct declarations with the same console name, without storing string IDs. It deduplicates shared command
structs. ContainsMetadata/AddMetadata are generated-code support APIs hidden from IntelliSense.
Metadata and parameter collections are immutable snapshots, owned by the registry/caller,
not static state. Repeated generation skips existing entries and preserves cached snapshots.

IsAvailable(string) checks active registration by case-insensitive name. Generating metadata
does not activate commands; creating/disposing modules does not change metadata. Active-entry
snapshots and metadata snapshots are invalidated independently. Reading variable values still
requires active registration. Execution schemas remain necessary for parsing; generated
active registrations omit descriptions, which belong to the optional declaration catalog.

## Registration modules

A compilation containing valid attributed owners must declare at least one [CmdModule]
partial class; otherwise the generator reports VARCOM001. No implicit module is generated.
Compilations without owners do not need a module.

[CmdModule(typeof(OwnerA), typeof(HandlerB))] on an empty partial class emits its typed
constructor and IDisposable implementation. Explicit lists preserve their declared order;
an empty list selects all local owners in fully qualified name order. The constructor
accepts the registry and ready-made instances, atomically registers them and never creates services.
Groups carry no lifetime enum: the application chooses their composition and activation.

Dispose is idempotent and removes registrations in reverse order. It never disposes owners.
Constructor failure rolls back previously registered members and owners. Queued calls retain
binding identity, so disposing/replacing a module cannot redirect old calls to new objects.
The generated module stores only its disposable registration group and clears it on Dispose.

Modules are concrete non-generic top-level partial classes without custom instance
constructors, base classes, command declarations or disposal members. Incorrect declarations,
duplicate member types and unavailable registrations produce VARCOM001. Automatic selection
is confined to the current compilation. Explicit external owner types are supported through
their assembly's public generated Register overload, with no runtime reflection.

Module construction/disposal allocates during initialization as registration already does;
it does not add work or allocations to Enqueue/Update. DI adapters and lifecycle activation
remain application responsibilities. No processor interface, module interface, resolver
callback, engine dependency or container dependency is introduced.

## Text boundary

Enqueue(string) accepts one or more invocations separated by semicolons outside quotes.
Tokenization and statement boundaries are handled internally in a single scan. There is
no public Split helper and the caller never needs to split commands manually.

The complete input is parsed before any of its invocations are enqueued. A syntax,
argument or lookup error rejects this submission without queueing a valid prefix;
previously queued calls remain unchanged. Execution failures are individual results, not
transaction rollback. Command factories/constructors run during Update, immediately before
the handler. Constructor failures are execution failures, not enqueue failures.

Empty segments between separators are ignored; input containing no commands is rejected.
Whitespace separates arguments; quoted empty strings are preserved. Within quotes,
backslash escapes quote/backslash; other backslashes remain literal. Newlines are
whitespace, not separators. Comments/files are host concerns.

Names use ASCII letters, digits, underscore or dot, begin with a letter, and may have a
leading +/-. Lookup is ordinal case-insensitive. No automatic +/- generation is performed.

Supported argument/variable values: CmdString, bool, int, float, double, enum. Boolean input is true/false (case-insensitive) or 1/0;
non-finite floats and
undefined enum values are rejected.
Numeric parsing/formatting use invariant culture. Missing required, malformed and extra
arguments reject the submission. Defaults apply only to explicitly optional parameters.
Command construction does not happen while accepting text. Recheck binding activity after
construction so a constructor cannot redirect its command to a replacement registration.
Typed submissions skip parsing, so domain checks belong to handlers and setters.

## Execution and lifetime

Update drains only the number of calls present at its start. Reentrant Update throws;
if attempted by a handler it becomes that call's failure. One failed handler does not stop
its peers. Commands enqueued during execution wait for the next Update. There are no
priorities, hidden threads or immediate execution API. The host owns single-thread access.

Acceptance is not execution success. Update optionally calls an Action<CommandResult>
for each processed invocation, in order. Without a callback the results are discarded.
There is no result array, ticket or completion wrapper. Callback exceptions propagate,
stop the current batch and leave the remaining calls queued; the host owns callback safety.

Variable reads via text are queued and observe preceding writes. Both queued and immediate reads return
a typed CommandValue. Use Get<T> for data or TryFormat for invariant text in caller-owned
space; ToString explicitly materializes a string. ReadVariable is immediate and does not
construct a command.

Disposal invalidates queued calls via binding identity. They cannot execute on a replacement
handler with the same type/name. Released bindings clear their owner delegates; externally
supplied payloads must avoid retaining game owners if that is unwanted. Varcom does not roll
back game side effects if an invocation throws. Services and their disposal belong to the host.

Exceptions at execution become ExecutionFailed. Expected business failures are returned by
the handler. CommandResult is a value type and cannot be null; default is an empty success.
A result can contain a value for GUI/logging. No streamed output subsystem is introduced.

## Verification

Runtime checks cover deferred execution, strict arguments/defaults, FIFO batches, continued
processing after failure, disposal/replacement, variable reads/writes/read-only protection,
atomic registration rollback, business failures, reentrancy, metadata snapshots and escaping.
Generator xUnit theories compile actual attributed sources and verify diagnostics for invalid forms.
A separate executable sample exercises generated bindings, not hand-written equivalents.

Not implemented: SARG migration, engine/DI adapters, persistent declaration catalogs, async,
network replication, configuration persistence, custom parsers, GUI and package publication.

## Allocation scope

Queued entries are structs inside Queue, not individual heap objects. Successful Enqueue
and handler results are structs. Empty Update and result dispatch do not allocate arrays
or wrappers. Each processor lazily owns a Queue<T> per concrete typed command. Global
pending entries reference that queue without boxing the command. The shared registry holds
handlers but never owns payload queues, so processors remain independent.

Release checks on x64 .NET 8 measure zero allocated bytes for warmed typed and text calls,
including escaped text, enums, numeric results, variable reads/writes, expected parse errors,
metadata reads and a cached result callback formatting into Span<char>. Handlers, callbacks
and callers remain responsible for their own allocations.

The processor accepts initialCapacity (default 128), used for execution, staging and typed
queues. First use of a typed queue, runtime cache initialization, registration and capacity
growth allocate. Numeric/name decoding uses processor-owned scratch space, initially 128
characters, growing for larger decoded tokens. Text values never refer to this scratch space.

CmdString retains the immutable submitted string and token boundaries. Enumeration,
Equals and TryCopyTo decode without allocating. ToString is explicit materialization.
A retained view keeps its entire original string alive, not just the argument range.
Normal string parameters/variables are rejected by the generator: use CmdString.

CommandValue stores scalars, enums and text without object boxing. CommandError is a struct;
expected diagnostics use static or registration-time strings. Messages identify the invalid
parameter and expected type without interpolating the received value on each rejection.
Dynamic user messages and exceptions can allocate. Varcom stores no result histories.

A disposed typed call still dequeues its data and reports Unavailable; this keeps the
remaining payloads aligned after re-registration. Typed queues contain no handler references;
registration disposal clears handler/delegate references in the binding.

## Architecture stage: explicit definitions and invocations

Responsibilities after the allocation stage:

- CommandRegistry owns name/type lookup, metadata snapshots and registration handles.
- CommandBinding owns definition lifetime through IsActive and Deactivate. Typed command,
  method and variable bindings each own only their own factory/handler/accessors.
- CommandLineParser validates text into a reusable processor-owned staging list. It never schedules work.
- CommandProcessor owns all buffers, global execution order, batching and result delivery.
- QueuedInvocation has explicit typed-buffer and text-argument alternatives; it no longer
  guesses the invocation kind by testing an object for a private queue interface.

Bindings have no reference to CommandProcessor or its queues. Processor-owned typed buffers
call the matching typed binding; disposal invalidates old calls and clears owner references.
Variable capability is explicit through an internal IVariableBinding, separate from activity.

Text invocations retain source ranges and parser metadata, not boxed command structs or
object arrays. Validation parses scalar arguments on the first lexical pass before acceptance; generated bindings
read them again during execution and construct commands on the stack. This trades a second
scalar parse for simple ownership without payload pools. Throughput still needs benchmarking.
Generated parameter metadata supplies closed generic parsers. Manual enum metadata without
these generated parsers uses registration-time reflection to construct its generic parser;
there is no reflection in the warmed enqueue/execute path.

exec and wait remain deferred features.

## Test organization

Core.Tests and Generator.Tests are xUnit projects, run using dotnet test or an IDE test
runner. Each Core behavior group has its own test class; invalid input cases are theories.
Assertions report expected values, sequences and exception types. Allocation checks are
isolated in a collection with DisableParallelization and perform assertions after measurement.
The sample remains an executable consumer; it is not a replacement for unit tests.
