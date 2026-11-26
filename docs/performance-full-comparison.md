# Full benchmark comparison after argument optimization — 2026-11-26

This reruns all **31 scenarios** after the local argument-validation optimization.
BenchmarkDotNet 0.15.8, .NET 8.0.33, SDK 8.0.133, Linux CachyOS, AMD Ryzen 7 9800X3D,
x64 RyuJIT, workstation GC. ShortRun: one process launch, three warmups and three
measured iterations. No builds, tests or other benchmark runners were run concurrently.

Before values come from the saved original CSV exports: baseline-operations.csv,
baseline-registry.csv and baseline-pipeline.csv. For all typed payload rows, the later
payload128-pipeline.csv provides the previous 4/56/128 B comparison. No historical
measurements were overwritten. After values come from one new full 31-case run.

The historical text pipeline had two registered command types; the current setup has
three, including the added 128-byte fixture. Job settings and payload/handler work match,
but this setup difference and process/JIT/hash-layout variation mean the percentages
are observed differences, not isolated causal effects of the optimization.

**Time change = (after / before - 1). Negative means lower measured time.** Typed queues,
EmptyUpdate, immediate reads and registry algorithms were not modified. Their differences
are controls and are not claimed as speedups or regressions based on one ShortRun.
For causal evidence about argument validation use the [multiple-launch comparison](performance-arguments.md).

## Full pipeline

Parameter is BatchSize. Every time is **per complete batch**, including all Enqueue calls
and one Update. It is not per command; divide by BatchSize to get the average per-command
cost. TypedSmall/TypedLarge/Typed128 carry 4/56/128 bytes, verified on this runtime.

| Scenario | Parameter | Before | After | Time change | After allocations |
| --- | ---: | ---: | ---: | ---: | ---: |
| TypedSmall | 1 | 47.99 ns | 44.51 ns | -7.3% | 0 B |
| TypedLarge | 1 | 55.79 ns | 58.91 ns | +5.6% | 0 B |
| Typed128 | 1 | 60.50 ns | 57.18 ns | -5.5% | 0 B |
| TextIndividual | 1 | 144.01 ns | 130.09 ns | -9.7% | 0 B |
| TextSequence | 1 | 142.74 ns | 128.38 ns | -10.1% | 0 B |
| TypedSmall | 32 | 1.280 us | 1.299 us | +1.5% | 0 B |
| TypedLarge | 32 | 1.591 us | 1.753 us | +10.2% | 0 B |
| Typed128 | 32 | 1.810 us | 1.856 us | +2.6% | 0 B |
| TextIndividual | 32 | 4.584 us | 4.212 us | -8.1% | 0 B |
| TextSequence | 32 | 3.544 us | 3.403 us | -4.0% | 0 B |
| TypedSmall | 128 | 5.282 us | 5.170 us | -2.1% | 0 B |
| TypedLarge | 128 | 7.129 us | 6.652 us | -6.7% | 0 B |
| Typed128 | 128 | 6.820 us | 6.848 us | +0.4% | 0 B |
| TextIndividual | 128 | 18.264 us | 16.697 us | -8.6% | 0 B |
| TextSequence | 128 | 13.648 us | 13.313 us | -2.5% | 0 B |
| TypedSmall | 256 | 10.062 us | 10.154 us | +0.9% | 0 B |
| TypedLarge | 256 | 12.736 us | 13.108 us | +2.9% | 0 B |
| Typed128 | 256 | 13.927 us | 13.836 us | -0.7% | 0 B |
| TextIndividual | 256 | 35.987 us | 32.160 us | -10.6% | 0 B |
| TextSequence | 256 | 26.996 us | 26.606 us | -1.4% | 0 B |

## Individual operations

Parameter is one complete operation. Text calls include Enqueue + Update, whereas immediate
reads bypass the queue. Queued reads consume results through a cached callback. EscapedText
measures delivery of a text view and its length, not copying the decoded text into a buffer.

| Scenario | Parameter | Before | After | Time change | After allocations |
| --- | ---: | ---: | ---: | ---: | ---: |
| EmptyUpdate | 1 operation | 1.80 ns | 1.87 ns | +3.7% | 0 B |
| Method | 1 operation | 94.29 ns | 91.10 ns | -3.4% | 0 B |
| VariableWrite | 1 operation | 169.84 ns | 153.69 ns | -9.5% | 0 B |
| VariableRead | 1 operation | 116.61 ns | 115.67 ns | -0.8% | 0 B |
| ImmediateVariableRead | 1 operation | 21.39 ns | 21.15 ns | -1.1% | 0 B |
| EscapedText | 1 operation | 169.06 ns | 150.29 ns | -11.1% | 0 B |
| EightExplicitArguments | 1 operation | 390.55 ns | 345.65 ns | -11.5% | 0 B |
| EightDefaultArguments | 1 operation | 125.51 ns | 119.84 ns | -4.5% | 0 B |

## Registry size

Parameter is number of registrations; each case performs one target submission and Update.
The target command name and handler work are fixed across registry sizes.

| Scenario | Parameter | Before | After | Time change | After allocations |
| --- | ---: | ---: | ---: | ---: | ---: |
| LookupAndExecute | 1 | 93.74 ns | 91.15 ns | -2.8% | 0 B |
| LookupAndExecute | 128 | 97.22 ns | 99.84 ns | +2.7% | 0 B |
| LookupAndExecute | 1024 | 95.55 ns | 93.18 ns | -2.5% | 0 B |

## Interpretation

- All 31 after cases report zero managed allocations after warmup. Input creation,
  registration and buffer growth are outside the measured work.
- Text submission is the path affected by the optimization: validating an already scanned
  token removes an additional lexical pass. Execution-side numeric parsing remains.
- The typed pipeline did not change logically. Do not interpret every typed-row delta as
  an improvement caused by changing the text parser; ShortRun is a survey, not a regression gate.
- This broad rerun is less precise than the focused validation using three process launches
  and eight iterations per launch. It supplements that evidence rather than replacing it.
- These handlers do minimal observable work; the figures do not include real game logic.

## Reproduction and raw data

```sh
dotnet run --project Tests/Benchmarks/Benchmarks.csproj -c Release -- --job short --filter '*' --artifacts /tmp/varcom-perf-full-after
```

See [benchmark instructions](../Tests/Benchmarks/README.md) for setup and units.
Raw after exports include Mean, Error, StdDev and Allocated:

- [All pipeline cases](../Tests/Benchmarks/Results/full-after-pipeline.csv)
- [All individual operations](../Tests/Benchmarks/Results/full-after-operations.csv)
- [All registry sizes](../Tests/Benchmarks/Results/full-after-registry.csv)

The complete original tables remain in [performance-baseline](performance-baseline.md).
