# Changelog

## 0.4.0-alpha.1 — 2026-11-27

- Add public CommandTokenizer and immutable CommandInvocation for registry-independent text preparation.
- Add single-call and ReadOnlySpan batch insertion and enqueueing, preserving call order.
- Use one execution queue; inserted commands run before the remainder in the current Update.
- Resolve text registrations and typed handlers at execution, allowing level loading and replacement before pending calls run.
- Delegate string preparation and pooled token ownership to a separate component; preserve warmed zero-allocation paths.
- Report empty tokenized calls as InvalidSyntax during execution and continue with subsequent calls.

Breaking behavior: command lookup and argument errors are now execution results rather than enqueue failures.
Pending calls use the current registration, including replacements. Both string overloads treat physical
newlines as separators and reject empty input. Syntax errors still reject a whole string submission atomically.
The application supplies exec loading and syntax-error reporting; wait remains deferred.

## 0.3.0-alpha.1 — 2026-11-26

- Store optional declaration metadata and active registrations in independent registry catalogs.
- Include commands without handlers and expose metadata before game objects exist.
- Generate an explicit GenerateMetadata extension; add GetMetadata and IsAvailable(name).
- Allocate declaration metadata only on request, without flags or static storage; deduplicate repeated calls.

## 0.2.0-alpha.1 — 2026-11-26

- Require an explicit CmdModule declaration and generate its typed constructor dependencies.
- Add CmdModule declarations for explicitly selected registration groups.
- Atomically register groups and dispose only their registration handles, without owning services.
- Support explicit referenced owners with existing generated registration; validate module declarations.

## 0.1.0-alpha.3 — 2026-11-26

- Validate arguments during the first lexical pass, removing redundant scanning.
- Preserve diagnostic priority, atomic rejection and warmed zero-allocation behavior.
- Add six regression cases for competing syntax, count, value and read-only errors.
- Add a separate BenchmarkDotNet project with 31 scenarios, including 128-byte payloads.
- Record baseline, repeated-process validation and complete before/after CSV results.
- Correct the analyzer release-table header present in the previous commit; no RS2007 suppression is needed.

No public API changes. Numeric conversion during execution remains; further performance
work is deferred until application profiling demonstrates a need.

## 0.1.0-alpha.2 — 2026-11-26

- Zero-allocation warmed text commands and expected parse failures within buffer capacity.
- CmdString arguments/variables replace string; CommandValue results replace object.
- CommandError is a struct; success uses CommandErrorCode.None.
- Configurable initial processor capacity, default 128; cached metadata snapshots.
- Text command construction now happens during Update, not Enqueue.
- Corrected analyzer release tracking table; builds no longer need RS2007 suppression.

Breaking API changes: use CmdString for textual parameters/variables; read results via
CommandValue.Get<T>() or TryFormat. Error is a value type; check IsSuccess/Error.Code.
Text command constructor exceptions are reported during Update.

exec and wait remain deferred. Automatic aggregate registration modules are not implemented.

## 0.1.0-alpha.1 — 2026-11-25

Initial implementation of the engine-independent Varcom runtime and source generator.

- Attribute-based struct commands, typed handlers, method commands and console variables.
- FIFO processing, next-update child commands and disposable scope registrations.
- Internal quote-aware command sequences with atomic rejection before enqueue.
- Source-generated registration overloads and compile-time diagnostics.
- xUnit behavior, lifetime, allocation and generator checks.

Known limits: string parsing and result-value boxing can allocate; automatic aggregate
constructor-injected binding modules are not implemented yet. The generator currently
emits registration overloads for individual handler/owner instances.
