---
paths:
  - Quake.UnitTests/**/*.cs
---

# Unit Test Rules (Quake.UnitTests)

MSTest 4. `Quake.Engine` is mutable statics, so tests share state.

## File structure
- `{Category}/{Behavior}Tests.cs`, or `{Category}/Real{Behavior}Tests.cs` for tests that need a real Quake install. `{Category}` is the `Quake.Engine` folder.
- `[TestClass] public sealed unsafe class` in `namespace Quake.UnitTests;`. No `[DoNotParallelize]` (set assembly-wide in `MSTestSettings.cs`).

## Tests
- Name: `Subject_Condition_ExpectedResult`; keep the original C name in the subject. Use `[DataRow]` for parameter sets.
- Float asserts take an explicit delta; buffer asserts use `CollectionAssert.AreEqual` with a message naming the loop variable.
- Port-fidelity: compare the engine against a local scalar transcription of the original C on seeded `Random` input. Every `*Simd` path needs one, looping lengths past the vector tail (e.g. `0..70`); reuse that scalar as the benchmark baseline.
- Bit-exact unless the optimization intentionally reorders float math.

## Engine state
- `EngineStateGuard` resets statics and native tables before each test. A class's `[TestInitialize]` (`Setup`) builds only what it needs; `[TestCleanup]` (`Teardown`) frees what it allocated.
- Never leave an engine static pointing at a stack frame or freed block.
- New `static readonly T*` engine tables must be classified in `EngineStateGuard`.
- Pass strings to engine statics via `TestHelpers.Ansi` (intentionally leaks). Put helpers in `TestHelpers.cs` only if identical across files.
- Wait with polling (`WaitForData`), never `Thread.Sleep`.

## Real-data tests
- Path comes from `configuration.json` via `TestConfiguration`. If unset or the file is missing, `Assert.Inconclusive`, never fail.
- Complement, don't replace, synthetic-fixture tests.

## Copyright
- Add the id Software header only to files containing ported original C code (e.g. a transcribed reference implementation), not to every test file.
