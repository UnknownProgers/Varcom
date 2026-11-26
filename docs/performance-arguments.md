# Argument validation optimization — 2026-11-26

Latest full rerun: [all 31 cases before/after](performance-full-comparison.md).

A local change in ArgumentParser and CommandLineParser removes one lexical pass during
Enqueue. Public API, queue storage, generated bindings and execution ordering are unchanged.
No payload cache, pool or additional runtime buffer was added.

Previously Enqueue scanned a statement to count arguments, then ArgumentParser.Validate
scanned its argument text again to validate conversions. Now each token is checked while
CommandLineParser already has its view. CompleteValidation checks excess/missing arguments
at the statement boundary. The first conversion error is retained until that boundary so
syntax, read-only and excess-argument errors preserve their original priority.

**Scalar conversion still happens at enqueue and again at execution.** This change removes
redundant lexical scanning, not all repeated numeric parsing. CommandArguments.Get is unchanged.

## End-to-end comparison, repeated processes

BenchmarkDotNet 0.15.8, .NET 8.0.33, SDK 8.0.133, Linux CachyOS, x64, AMD Ryzen 7 9800X3D.
Each case includes Enqueue + Update using the same generated BenchmarkOwner and input.
Three process launches per case, five warmup iterations and eight measured iterations per
launch. Before/after ran sequentially, without concurrent builds/tests/benchmark runs.

Before runtime: files from base commit 90abdf5. Both variants used the same current benchmark
suite. Copies were built in /tmp/varcom-argument-confirm/before and /after; the working tree
was not switched back and forth. BenchmarkDotNet emitted a nonfatal temp-location warning;
these were ordinary console projects, with no xUnit shadow copying. All builds and measured
processes completed successfully.

Before:

| Method                 | Mean     | Error   | StdDev  | Allocated |
|----------------------- |---------:|--------:|--------:|----------:|
| EscapedText            | 172.6 ns | 1.91 ns | 2.42 ns |         - |
| EightExplicitArguments | 390.9 ns | 1.52 ns | 1.92 ns |         - |
| EightDefaultArguments  | 124.4 ns | 1.03 ns | 1.35 ns |         - |

After:

| Method                 | Mean     | Error   | StdDev  | Allocated |
|----------------------- |---------:|--------:|--------:|----------:|
| EscapedText            | 145.9 ns | 1.96 ns | 2.48 ns |         - |
| EightExplicitArguments | 343.5 ns | 3.21 ns | 3.94 ns |         - |
| EightDefaultArguments  | 124.4 ns | 1.96 ns | 2.55 ns |         - |

In this confirmation series, time decreased by about 12% for eight explicit arguments and
15% for escaped text delivery. The defaults control is effectively unchanged. These are
machine-specific measured results, not universal speedup guarantees. Error is half of the
reported 99.9% confidence interval; raw CSV retains Error and StdDev. EscapedText delivers
a CmdString view and reads its length; it does not copy decoded characters to a destination.

## Initial comparison, wider set of cases

One launch per case, five warmups and twelve measured iterations. Same runtime/machine.
This earlier series found a smaller gain for eight explicit arguments (about 8%); include
both series rather than treating one process's exact figure as a universal improvement.
VariableWrite and Method were measured only in this wider single-launch series.

Before:

| Method                 | Mean     | Error   | StdDev  | Allocated |
|----------------------- |---------:|--------:|--------:|----------:|
| Method                 | 101.4 ns | 0.33 ns | 0.26 ns |         - |
| VariableWrite          | 175.4 ns | 0.62 ns | 0.48 ns |         - |
| EscapedText            | 174.4 ns | 0.73 ns | 0.53 ns |         - |
| EightExplicitArguments | 391.3 ns | 2.71 ns | 2.12 ns |         - |
| EightDefaultArguments  | 123.2 ns | 0.75 ns | 0.54 ns |         - |

After:

| Method                 | Mean      | Error    | StdDev   | Allocated |
|----------------------- |----------:|---------:|---------:|----------:|
| Method                 |  91.10 ns | 0.285 ns | 0.206 ns |         - |
| VariableWrite          | 152.20 ns | 1.013 ns | 0.670 ns |         - |
| EscapedText            | 143.23 ns | 0.614 ns | 0.406 ns |         - |
| EightExplicitArguments | 361.20 ns | 2.305 ns | 1.799 ns |         - |
| EightDefaultArguments  | 123.95 ns | 1.462 ns | 1.141 ns |         - |

## Diagnostic phase split

The --phases runner uses 128-command batches, 2,000 warmup batches and five samples of
10,000 batches. Values below are medians in ns/command. Registration/input construction
are excluded. Stopwatch and enqueue-loop overhead are included and amortized across the
batch; these diagnostics do not replace BenchmarkDotNet and need not sum to its mean.

| Input | Enqueue before | Enqueue after | Update before | Update after |
| --- | ---: | ---: | ---: | ---: |
| Eight defaults | 85.75 | 82.41 | 36.74 | 37.39 |
| Eight explicit numbers | 234.08 | 188.73 | 183.01 | 185.08 |

The expected effect is concentrated in Enqueue. No execution-side optimization was made.
Both phase runners measured zero allocated bytes in their timed loops; MemoryDiagnoser
also reported zero managed allocations in all end-to-end cases, before and after.

## Correctness and scope

- Six regression cases check competing errors: excess arguments, invalid provided values,
  missing values, unterminated strings and writes to read-only variables. They also verify
  atomic sequence rejection while previously queued work still executes.
- 66 Core xUnit cases passed in Release and Debug, including exact allocation checks.
- 14 Generator cases passed in Release. No warning suppression is needed.
- Typed payload storage, 128-byte fixture, capacity rules, lifetime invalidation, next-Update
  children and callback behavior are unchanged.
- Only argument validation was optimized; no claim is made about eliminating the remaining
  execution conversion or about overall game-frame performance.

## Reproduction

Use the same benchmark suite for both runtime versions. The before snapshot can be recreated
by copying the current project layout and replacing Core/ArgumentParser.cs and
Core/Parsing/CommandLineParser.cs with their versions from commit 90abdf5.

```sh
# Diagnostic split, one version at a time.
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --phases

# Initial five-case comparison.
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --job short --warmupCount 5 --iterationCount 12 --filter '*OperationBenchmarks.Eight*' '*OperationBenchmarks.VariableWrite' '*OperationBenchmarks.EscapedText' '*OperationBenchmarks.Method'

# Confirmation, same command in each isolated snapshot.
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --job short --launchCount 3 --warmupCount 5 --iterationCount 8 --filter '*OperationBenchmarks.Eight*' '*OperationBenchmarks.EscapedText'
```

Raw results:

- [Initial before](../Tests/Benchmarks/Results/arguments-before.csv)
- [Initial after](../Tests/Benchmarks/Results/arguments-after.csv)
- [Confirmation before](../Tests/Benchmarks/Results/arguments-confirm-before.csv)
- [Confirmation after](../Tests/Benchmarks/Results/arguments-confirm-after.csv)
- [Phases before](../Tests/Benchmarks/Results/arguments-phases-before.csv)
- [Phases after](../Tests/Benchmarks/Results/arguments-phases-after.csv)
