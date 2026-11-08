---
paths:
  - Quake.Benchmarks/**/*.cs
---

# Benchmark Rules (Quake.Benchmarks)

## File structure
- `{Category}/{Method}Benchmarks.cs`, one per benchmarked method. `{Category}` is the `Quake.Engine` folder the method lives in.
- Shared fixtures/helpers go in the `{Category}` folder (or `Common/` if cross-category), referenced not copied.
- Variant implementations go in a sibling `{Method}Variants.cs`.

## Inside each class
- `#region Setup / Teardown` first: fields, `[GlobalSetup]`, `[GlobalCleanup]` (and iteration variants). Factor into a shared base/helper if reused by several files.
- Original (ported C) implementation is the `[Benchmark(Baseline = true)]`; alternatives follow. For several methods in one class, pair baselines with `[BenchmarkCategory]` + `[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]`.
- Verify equal results in setup or via a unit test.
