# Stage 1 work log

- Inspected the repository, SDK selection and existing Core project to avoid assuming a
  layout or implementation that is not present.
- Recorded a minimal engine-independent API and explicit execution/lifetime rules.
- Added compile-only contracts and consumer examples rather than a placeholder runtime.
- Kept Core and the solution unchanged; runtime and generator setup belong to stage 2.
- Verified the contracts and examples with the installed C# 12 compiler against .NET
  8 reference assemblies (nullable enabled, warnings treated as errors): passed.
- Full dotnet build could not restore the SDK-selected targeting dependencies because
  NuGet network access is unavailable in this session. Direct compilation used the
  installed Microsoft.NETCore.App.Ref 8.0.33 and wrote artifacts only under /tmp.
- Checked Git whitespace and repository status. No commits or pushes were made.

## Runtime implementation

- Incorporated review: removed mandatory CommandContext, ICommandProcessor and DI abstractions.
- Added behavior checks first and observed failure because the runtime did not exist.
- Implemented concrete registry/processor, strict parsing, variable bindings and disposal.
- Added an attributed consumer first; verified failure without generated registration.
- Implemented incremental registration generation and checks for invalid declarations.
- Replaced the obsolete API sketch with real Core contracts and a separate executable sample.
- Added generator packaging beside the runtime; publication and SARG migration remain separate.

### Validation

- 15 runtime behavior checks passed, including collection of a disposed variable owner
  while its old invocation remains queued.
- 9 generator compilation/diagnostic checks passed using Roslyn and real attributed sources.
- Executable generated consumer passed: handler, Cmd method and Var on existing instances.
- Release NuGet pack passed with runtime, generator, README and license included.
- A clean PackageReference-only consumer restored the local package into an isolated cache
  and executed generated registrations successfully.
- Final source compilation and packaging produced no warnings/errors; whitespace checked.
- No publication, SARG edits, commits or pushes performed. Existing index entries were not changed.

## Lightweight results and queue

- Updated behavioral checks first; observed expected compilation failures for the removed API.
- Replaced result inheritance with readonly CommandResult; removed submission/ticket/completion
  types and reused CommandResult for immediate variable discovery.
- Replaced heap-allocated pending calls with structs and result arrays with optional inline
  callbacks. Removed the extra LINQ argument-copy array on text submission.
- Preserved the user's common Varcom namespace and aligned generator symbol lookup with it.
- Allocation regression initially measured approximately 72 bytes per typed call. Identified
  extra boxing in null/type checks and reduced it to 24 bytes per small struct call in Release.
- Empty Update measured zero allocated bytes. Remaining payload boxing is explicit, not hidden
  behind a zero-allocation claim. Callback exceptions and result-discard behavior are documented.
- Final validation: 19 runtime checks and 9 generator checks passed; generated Sample ran
  successfully and Release package built without warnings. No implicit usings introduced.
- No commits, pushes, package publication or SARG changes performed.

## Zero-allocation typed payloads

- Traced 24 bytes/call to a small struct payload boxed by the object-based queue.
- Replaced direct typed payload storage with per-processor Queue<T> lanes. Global FIFO
  references a lane; handlers receive T directly. No unsafe buffers or per-call wrappers.
- Preserved already-boxed interface dispatch, text submissions, handler failures and disposal.
- Added checks for independent processors, mixed text/typed ordering after replacement,
  large structs/reference fields, cached result callbacks and disposal of typed handlers.
- Release allocation checks measure 0 bytes/call and 0 bytes for the warmed mixed batch loop.
  Cold setup/capacity growth, text parsing, result-value boxing and user allocations are excluded.

## Struct-only commands

- Added negative compilation checks and observed class submissions incorrectly compile.
- Enforced struct, ICommand on Enqueue, registry registration, handlers and typed queues.
- Removed reference-type/null/runtime-type fallback; replaced polymorphic regression with
  compile-time rejection checks. Kept zero-allocation typed storage and shared FIFO behavior.

## Rollback of string-to-typed experiment

- Reverted processor-aware binding delegates and the internal ArgumentsCommand adapter.
- Restored the preceding string payload path; preserved struct-only constraints, zero-allocation
  direct typed queues and prior user edits. No Git reset, staging changes or commits.

## Architecture stage 1

- Added behavioral checks for factory disposal, parsed parents producing typed/text children,
  and deferred variable getters; confirmed baseline behavior before refactoring.
- Extracted typed/method/variable binding implementations with one explicit IsActive lifecycle.
- Kept the registry independent of the processor and moved text orchestration to CommandLineParser.
- Replaced ambiguous Pending.Payload dispatch with explicit QueuedInvocation alternatives;
  extracted processor-owned TypedCommandBuffer storage. No public API or generator changes.
- Preserved existing parsing/boxing as the next separate allocation task, rather than mixing
  allocation optimization with architectural refactoring.

## Internal command sequences

- Removed public CommandText.Split; the lexical scanner is now internal and handles tokens
  and semicolon boundaries in one pass without a caller-side splitting loop.
- Enqueue(string) parses the entire sequence before appending invocations, preserving existing
  queued work if syntax, lookup or argument validation fails.
- Verified quoted semicolons/empty arguments/escapes, empty segments, atomic rejection and
  child sequences deferring to the next Update. Typed zero-allocation checks remain at zero.
- All 27 runtime and 12 compilation checks passed, as did the generated consumer Sample.

## xUnit migration

- Replaced both executable test runners with xUnit/Microsoft.NET.Test.Sdk projects.
- Split Core checks into processor, text, registry, variable, lifetime and allocation classes;
  replaced Check/console reporting/manual exception checks with xUnit assertions.
- Preserved all previous scenarios; invalid argument variants are individually discovered theories.
- Added a nonparallel allocation collection; no asserts or test-output calls run inside measured loops.
- Moved generator declaration/constraint cases to MemberData theories compiling real consumer code.
- Release dotnet test passed: 31 Core tests and 12 Generator tests. Runtime code unchanged.

## Final verification — 0.1.0-alpha.1

- Aligned generator references and compilation fixtures with the current Varcom.Contracts namespace.
- Applied JetBrains CleanupCode 2026.3.0.2, Built-in: Reformat Code, using the project's
  Varcom.sln.DotSettings and EditorConfig. Repeated pass changed zero source files.
- All 43 xUnit tests passed in Debug and Release; Sample executed successfully.
- Verified NuGet version/content and installed it into an isolated PackageReference-only consumer;
  generated registration and command/variable execution passed without project references.
- Added version notes and ignored personal Rider state/artifacts; shared settings are retained.
- Saved the requested global Rider formatting skill and agent instruction outside the repository.

## Deferred features — 2026-11-26

- Implement `exec` and `wait` later; neither belongs to the current implementation stage.
- `exec` is a confirmed planned feature. Its command insertion and nested execution semantics
  will be designed when implementation begins.
- The scope and timing semantics of `wait` remain to be designed; no global simulation pause
  or queue-blocking behavior is decided by this note.

## Zero-allocation text execution — 2026-11-26

- Added immutable CmdString source views and typed CommandValue result storage.
- Replaced object[] arguments and boxed parsed commands with validated source ranges.
- Command constructors now run during Update, immediately before their handler.
- Reuse processor staging/scratch buffers; initial queue capacity defaults to 128.
- Preserve atomic rejection, FIFO, next-update children and disposed-binding identity.
- Cache metadata snapshots and prepare parameter diagnostics at registration.
- CommandError is a struct; standard rejections and ready-made business errors allocate zero.
- 60 Core and 14 Generator cases passed in both configurations; zero-allocation tests
  cover generated handlers/methods, all supported argument types, reads/writes and TryFormat.
- Repeated Release profile measured 0 B for successful paths and expected parse failures.
- Compared 100,000 random inputs with the original scanner; lexical behavior matched.
- Applied JetBrains Reformat Code with repository settings. Initial checks used temporary
  RS2007 suppression; the malformed release-table header was subsequently corrected.

## Release verification — 0.1.0-alpha.2

- Set the package and README version to 0.1.0-alpha.2; recorded breaking API changes.
- Ordinary Release build passed with zero warnings and errors, without NoWarn suppression.
- All 74 Release xUnit cases passed, including warmed zero-allocation checks.
- Verified NuGet version/content and ran the generated sample through an isolated
  PackageReference-only consumer using the new package.
- Applied project Rider/ReSharper formatting and checked Git whitespace.
