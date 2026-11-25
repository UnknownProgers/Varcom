# Changelog

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
