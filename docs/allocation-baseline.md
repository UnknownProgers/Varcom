# Allocation baseline — 2026-11-26

Measured against `c424729` in Release on .NET 8.0.33, x64, SDK 8.0.133.
No runtime or generator implementation changes were made for this baseline.

## Method

A standalone console harness references the freshly built Release Varcom.dll.
Each case warms up for 2,000 iterations and measures 10,000 iterations using
GC.GetAllocatedBytesForCurrentThread. Enqueue and the following Update are measured
separately. A second process run reproduced every result below.

Inputs, delegates, registrations and processor are created before measurement.
Strings are constants; handlers and setters return empty successful results. The numeric
variable holds 12345. There are four bindings: add(int), ping(), echo(string), value(int).
Control delegates allocate zero. Tokenize is invoked through a cached strongly typed
delegate created once using reflection; reflection is outside measurement.

These are allocated bytes, not retained memory or timing benchmarks. Warmup excludes
initialization and queue growth. Handler allocations and caller-created input strings
are not included. Zero allocation is not a guarantee for cold or arbitrarily large batches.

## Original baseline results (before optimization)

Bytes per operation; the execution column measures the following Update.

| Operation                         | Enqueue / direct call | Execution |
|-----------------------------------|----------------------:|----------:|
| Empty Update                      |                     0 |         — |
| Typed Add                         |                     0 |         0 |
| add 12345                         |                   496 |         0 |
| ping                              |                   384 |         0 |
| echo hello                        |                   448 |         0 |
| echo with quoted and escaped text |                   464 |         0 |
| value 12345                       |                   472 |         0 |
| value (read)                      |                   416 |        56 |
| add 12345; ping; value 12345      |                   856 |         0 |
| add bad (rejected)                |                   592 |         0 |
| missing (unknown command)         |                   512 |         0 |
| ReadVariable("value")             |                    24 |         — |
| GetEntries, four bindings         |                   304 |         — |
| CommandResult.Ok(12345)           |                    24 |         — |
| Tokenize("add 12345") alone       |                   376 |         — |
| Tokenize("ping") alone            |                   344 |         — |

## Attribution from code and isolated measurements

- CommandText.Tokenize creates lists, their backing arrays, a StringBuilder and its
  character buffer, token strings, and a final unused empty token list.
- For add 12345, the measured 496 bytes consist of 376 bytes tokenization, a 40-byte
  ParsedCommand[1], a 32-byte object[1], a 24-byte boxed int argument and a 24-byte
  boxed Add struct. Sizes apply to this runtime/architecture and this harness command.
- For ping, tokenization is 344 bytes and ParsedCommand[1] adds 40 bytes.
- Array.Empty<T>() returns a shared empty array; it does not allocate on every call.
- A variable read creates a 32-byte argument array, then discards it in favor of
  Array.Empty<object?>(). Execution boxes the int (24 bytes) and formats 12345 into
  a string (32 bytes), accounting for the measured 56 bytes.
- ReadVariable returns the raw int through object-valued CommandResult.Value: 24-byte
  boxing. The same applies to CommandResult.Ok(int); empty success does not allocate.
- GetEntries constructs a fresh metadata snapshot using LINQ, entry objects, an array
  and a read-only wrapper. Registration itself is outside the hot-path measurements.
- Error paths create error objects and sometimes diagnostic strings. No zero-allocation
  error-path guarantee is proposed.

## Next changes, in order

1. Reduce lexical scanning allocations while preserving quotes, escapes, empty tokens
   and semicolon behavior. Compare against this baseline before changing binding contracts.
2. Remove intermediate parsed-sequence allocations while preserving atomic rejection:
   invalid sequences must not partially enter the execution queue.
3. Address object[] arguments, numeric boxing and boxed typed payloads on the text path
   together with generated bindings. Do not make bindings depend on the processor.
4. Remove discarded variable-read arguments; assess metadata snapshot caching separately.
5. Preserve warmed typed zero-allocation tests. Measure speed only after allocation changes
   are verified; do not infer throughput from these byte counts.

String-valued arguments retained by commands may need an owned string. Numeric result
boxing requires an explicit API decision; do not introduce result hierarchies or pools
merely to hide it. exec and wait remain deferred.

## Verification and limitations

31 Core and 12 Generator xUnit tests passed in Release with `-p:NoWarn=RS2007`.
The selected SDK 8 cannot read Varcom.slnx, so test projects were invoked individually.
Without suppression, the existing AnalyzerReleases.Unshipped.md table causes RS2007;
this baseline does not fix or claim a clean default build.

The temporary harness is /tmp/varcom-allocation-profile/Program.cs and runs with:
`dotnet run --project /tmp/varcom-allocation-profile/Profile.csproj -c Release --no-restore`.
It references Core/bin/Release/net8.0/Varcom.dll; build Core first. The harness is a local
measurement artifact and is not part of the library or NuGet package.

## Tokenizer optimization — 2026-11-26

Removed StringBuilder and its buffer. Plain tokens use substrings of the original
input; quoted/escaped tokens use string.Create with the decoded length counted by
the scanner. Token lists are created lazily, without an unused final list.
Public contracts, binding payloads and queue semantics are unchanged. No pools or
shared mutable buffers were introduced. Quoted tokens require a second decoding pass;
throughput has not been benchmarked, so this change claims fewer allocations only.

Same harness and runtime, bytes allocated in Enqueue:

| Input                             | Baseline | After tokenizer change |
|-----------------------------------|---------:|-----------------------:|
| add 12345                         |      496 |                    360 |
| ping                              |      384 |                    216 |
| echo hello                        |      448 |                    312 |
| echo with quoted and escaped text |      464 |                    328 |
| value 12345                       |      472 |                    336 |
| value (read)                      |      416 |                    248 |
| add 12345; ping; value 12345      |      856 |                    720 |

Tokenize alone: add 12345 decreased from 376 to 240 bytes; ping from 344 to 176 bytes.
Execution allocations are unchanged, including zero for warmed typed commands.
The remaining lists, strings, ParsedCommand array and object-based payloads still
allocate; this is not a zero-allocation text parser.

Added ten lexical regression cases and three allocation-budget cases. A local
differential check compared 100,000 fixed-seed random inputs against the original
scanner, matching tokens, statement boundaries and syntax-error status.

## Zero-allocation text path — 2026-11-26

Replaced materialized tokens with CmdString source views; object arguments/results
with typed CommandArguments/CommandValue; boxed text command payloads with construction
immediately before execution. CommandError is now a struct, and parameter diagnostic
strings are prepared at registration. The processor reuses a staging list and decoding
scratch space, preserving atomic rejection. Metadata snapshots are cached until registration
changes. initialCapacity defaults to 128 and buffers grow when necessary.

The migrated harness is /tmp/varcom-allocation-profile-zero/Program.cs. Rebuild Core and run:
`dotnet run --project /tmp/varcom-allocation-profile-zero/Profile.csproj -c Release --no-restore`.
It stores measured results as CommandValue, not object, so the harness does not box them.

On .NET 8.0.33 x64, after warmup: typed Add, text add, ping, plain/escaped echo, variable
write/read, command sequences, ReadVariable, cached GetEntries and numeric Ok all measure
0 B for both enqueue/direct call and execution. Expected invalid/unknown input is also
covered by zero-allocation tests.

End-to-end tests use actual generated registration and exercise all supported argument
types, defaults, text lifetimes, initial capacity/growth, numeric/enum results and TryFormat.
First-use caches, registration, growing buffers, input-string construction, explicit ToString,
dynamic user diagnostics and exceptions remain outside the warmed zero-allocation guarantee.
Text views retain their entire source string. Scalar arguments are validated at enqueue and
parsed again at execution; no throughput claim is made before the performance stage.

Final validation: 60 Core and 14 Generator xUnit cases passed in both Debug and Release.
The zero-allocation tests use actual generated bindings, and the new scanner matched the
original on 100,000 fixed-seed random strings. The standalone Release profile measured
0 B in every listed operation, including invalid arguments and unknown commands.
The initial measurements used RS2007 suppression. The release-table header has since
been corrected: the current Release build and all 74 tests pass without suppression.
