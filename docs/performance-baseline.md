# Performance baseline — 2026-11-26

Latest full rerun: [all 31 cases before/after](performance-full-comparison.md).

Follow-up: [single-pass argument validation optimization](performance-arguments.md).
This document retains the original unoptimized measurements.

Runtime base: Varcom 0.1.0-alpha.2, commit `90abdf5`. No runtime implementation was changed
for these measurements. Added benchmark fixtures and corrected the analyzer release-table
header present in this checkout, so builds run without diagnostic suppression.

BenchmarkDotNet 0.15.8, .NET 8.0.33, SDK 8.0.133, x64 RyuJIT, concurrent workstation GC.
Machine: Linux CachyOS, AMD Ryzen 7 9800X3D, 8 physical / 16 logical cores.
ShortRun: one process launch, three warmup iterations, three measured iterations per case.

Registration, input construction and buffer growth are outside timed work. Each pipeline
measurement includes enqueueing the full batch followed by one Update. Handlers perform
minimal observable work, adding one input value to a counter. This is infrastructure cost,
not a measurement of game logic. Mean in pipeline tables is **per batch**, not per command.

The first run measured 27 cases. After the requested 128-byte payload was added, a second
run measured all three typed payloads in 12 cases. The final suite has 31 unique cases.
The original pipeline setup had two registered command types; the second has three.
Use the second table for comparisons between payload sizes, not mixed rows from both runs.

MemoryDiagnoser reported no managed allocations for all 39 measured cases (shown as `-`).
Exact zero-allocation regression checks are also covered by Core.Tests. These results apply
after warmup within the available capacity, with the provided allocation-free handlers.

## Typed payload comparison including 128 bytes

SmallCommand is 4 bytes, LargeCommand is 56 bytes and Payload128Command is 128 bytes on
this runtime. Both larger payloads contain one CmdString reference view. All handlers do
the same arithmetic. Setup verifies sizeof(Payload128Command) == 128.

| Method     | BatchSize | Mean         | Error        | StdDev    | Ratio | Allocated | Alloc Ratio |
|----------- |---------- |-------------:|-------------:|----------:|------:|----------:|------------:|
| **TypedSmall** | **1**         |     **47.99 ns** |     **0.999 ns** |  **0.055 ns** |  **1.00** |         **-** |          **NA** |
| TypedLarge | 1         |     55.79 ns |     3.563 ns |  0.195 ns |  1.16 |         - |          NA |
| Typed128   | 1         |     60.50 ns |     3.132 ns |  0.172 ns |  1.26 |         - |          NA |
|            |           |              |              |           |       |           |             |
| **TypedSmall** | **32**        |  **1,280.13 ns** |    **71.579 ns** |  **3.923 ns** |  **1.00** |         **-** |          **NA** |
| TypedLarge | 32        |  1,590.52 ns |   101.494 ns |  5.563 ns |  1.24 |         - |          NA |
| Typed128   | 32        |  1,809.99 ns |   281.843 ns | 15.449 ns |  1.41 |         - |          NA |
|            |           |              |              |           |       |           |             |
| **TypedSmall** | **128**       |  **5,282.08 ns** |   **111.081 ns** |  **6.089 ns** |  **1.00** |         **-** |          **NA** |
| TypedLarge | 128       |  7,129.12 ns |   435.756 ns | 23.885 ns |  1.35 |         - |          NA |
| Typed128   | 128       |  6,820.34 ns |   202.307 ns | 11.089 ns |  1.29 |         - |          NA |
|            |           |              |              |           |       |           |             |
| **TypedSmall** | **256**       | **10,062.10 ns** | **1,234.376 ns** | **67.660 ns** |  **1.00** |         **-** |          **NA** |
| TypedLarge | 256       | 12,735.80 ns | 1,163.451 ns | 63.773 ns |  1.27 |         - |          NA |
| Typed128   | 256       | 13,927.11 ns |   995.753 ns | 54.581 ns |  1.38 |         - |          NA |

At BatchSize 128, the measured means are 41.27 ns/command (4 B), 55.70 ns/command (56 B),
and 53.28 ns/command (128 B), including the command's share of Update. The non-monotonic
56/128 B result does not establish that larger structs are faster: these are complete
pipelines with different generic types, dictionary hash layouts, JIT code and copies,
not an isolated memory-copy test. ShortRun's within-process error does not capture all
between-process/environment variation. Use longer, multiple-launch runs before acting
on small differences.

## Text versus typed batches — initial run

| Method         | BatchSize | Mean         | Error        | StdDev     | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------- |---------- |-------------:|-------------:|-----------:|------:|--------:|----------:|------------:|
| **TypedSmall**     | **1**         |     **45.30 ns** |     **0.677 ns** |   **0.037 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| TypedLarge     | 1         |     54.56 ns |     2.715 ns |   0.149 ns |  1.20 |    0.00 |         - |          NA |
| TextIndividual | 1         |    144.01 ns |     6.042 ns |   0.331 ns |  3.18 |    0.01 |         - |          NA |
| TextSequence   | 1         |    142.74 ns |     4.390 ns |   0.241 ns |  3.15 |    0.01 |         - |          NA |
|                |           |              |              |            |       |         |           |             |
| **TypedSmall**     | **32**        |  **1,323.91 ns** |     **7.770 ns** |   **0.426 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| TypedLarge     | 32        |  1,575.39 ns |   110.263 ns |   6.044 ns |  1.19 |    0.00 |         - |          NA |
| TextIndividual | 32        |  4,583.59 ns |   223.642 ns |  12.259 ns |  3.46 |    0.01 |         - |          NA |
| TextSequence   | 32        |  3,543.92 ns |   273.770 ns |  15.006 ns |  2.68 |    0.01 |         - |          NA |
|                |           |              |              |            |       |         |           |             |
| **TypedSmall**     | **128**       |  **5,287.69 ns** |   **301.272 ns** |  **16.514 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| TypedLarge     | 128       |  6,188.21 ns |   420.405 ns |  23.044 ns |  1.17 |    0.00 |         - |          NA |
| TextIndividual | 128       | 18,264.08 ns |   553.852 ns |  30.359 ns |  3.45 |    0.01 |         - |          NA |
| TextSequence   | 128       | 13,648.47 ns |   119.785 ns |   6.566 ns |  2.58 |    0.01 |         - |          NA |
|                |           |              |              |            |       |         |           |             |
| **TypedSmall**     | **256**       | **10,463.78 ns** |   **920.486 ns** |  **50.455 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| TypedLarge     | 256       | 12,866.79 ns | 1,207.081 ns |  66.164 ns |  1.23 |    0.01 |         - |          NA |
| TextIndividual | 256       | 35,986.76 ns | 3,486.222 ns | 191.092 ns |  3.44 |    0.02 |         - |          NA |
| TextSequence   | 256       | 26,995.57 ns |   662.464 ns |  36.312 ns |  2.58 |    0.01 |         - |          NA |

At BatchSize 128, individual text submissions averaged 18.26 us per batch; one prebuilt
sequence averaged 13.65 us. This is about 25% less time in this survey, with the same
handler work. The sequence shares parsing/queue submission overhead across commands.
The initial typed small batch averaged 5.29 us. No production speedup is claimed: these
are different submission modes of the existing runtime, not before/after code changes.

## Individual operations

| Method                 | Mean       | Error      | StdDev    | Allocated |
|----------------------- |-----------:|-----------:|----------:|----------:|
| EmptyUpdate            |   1.802 ns |  0.1693 ns | 0.0093 ns |         - |
| Method                 |  94.292 ns | 12.0277 ns | 0.6593 ns |         - |
| VariableWrite          | 169.840 ns | 11.4728 ns | 0.6289 ns |         - |
| VariableRead           | 116.611 ns | 12.0231 ns | 0.6590 ns |         - |
| ImmediateVariableRead  |  21.395 ns |  1.9216 ns | 0.1053 ns |         - |
| EscapedText            | 169.064 ns | 14.5099 ns | 0.7953 ns |         - |
| EightExplicitArguments | 390.552 ns | 51.7451 ns | 2.8363 ns |         - |
| EightDefaultArguments  | 125.513 ns | 14.9939 ns | 0.8219 ns |         - |

Queued variable reads use a cached result callback. Other operations observe owner state;
immediate reads return CommandResult directly. EscapedText measures delivery of a text
view and reading its length, not copying the decoded text into a destination buffer.

EightExplicitArguments and EightDefaultArguments invoke the same handler with the same
values. Their means are 390.55 versus 125.51 ns. The difference includes longer lexical
scans, validation, numeric conversions and reading supplied arguments. It does not isolate
the second parse or prove its share of total time.

## Registry size

| Method           | RegisteredCommands | Mean     | Error    | StdDev   | Allocated |
|----------------- |------------------- |---------:|---------:|---------:|----------:|
| **LookupAndExecute** | **1**                  | **93.74 ns** | **1.692 ns** | **0.093 ns** |         **-** |
| **LookupAndExecute** | **128**                | **97.22 ns** | **5.690 ns** | **0.312 ns** |         **-** |
| **LookupAndExecute** | **1024**               | **95.55 ns** | **3.357 ns** | **0.184 ns** |         **-** |

The target name is constant; extra registrations do no work during the measurement.
This survey shows no substantial increase from 1 to 1024 registrations. It does not
measure registration/unregistration time, collision-heavy inputs or concurrent access.

## Conclusions and next optimization target

- The measured typed pipeline remains zero-allocation for 128-byte payloads and 256-command
  warmed batches. This does not cover initialization, buffer growth or user allocations.
- Registry size is not a demonstrated bottleneck in these scenarios.
- Text argument processing is the strongest candidate for further profiling. Inspect
  CommandLineParser, ArgumentParser.Validate and CommandArguments.Get together. Current
  arguments are validated at enqueue and read/parsed again at execution; measure those
  phases separately before introducing storage or changing queue semantics.
- No runtime optimization was merged based solely on a ShortRun difference. This report
  establishes the reproducible baseline for evaluating one focused change at a time.

## Reproduction and raw results

See [benchmark setup and units](../Tests/Benchmarks/README.md). Exact commands used:

```sh
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --job short --filter '*' --artifacts /tmp/varcom-perf-baseline
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --job short --filter '*PipelineBenchmarks.Typed*' --artifacts /tmp/varcom-perf-payload128
```

The first command now includes the newly added 128-byte cases (31 total); the original
27-case snapshot is retained in the CSV files below. Run only one benchmark process at
a time, without builds/tests running concurrently.

Raw BenchmarkDotNet CSV exports, including Error and StdDev:

- [Initial operations](../Tests/Benchmarks/Results/baseline-operations.csv)
- [Initial pipeline](../Tests/Benchmarks/Results/baseline-pipeline.csv)
- [Initial registry](../Tests/Benchmarks/Results/baseline-registry.csv)
- [Typed 4/56/128 B comparison](../Tests/Benchmarks/Results/payload128-pipeline.csv)
