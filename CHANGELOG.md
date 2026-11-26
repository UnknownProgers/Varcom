# Changelog

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
