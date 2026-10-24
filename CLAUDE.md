# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

SDK is pinned by `global.json` (10.0.103, roll-forward `feature`). Solution file is `Log0.slnx`.

```bash
dotnet build Log0.slnx
dotnet test Log0.slnx
dotnet test tests/Log0.Tests --filter "FullyQualifiedName~AsyncMethodTests"
dotnet run --project samples/Log0.Sample -- --debug
dotnet pack src/Log0 -c Release
```

Tests use xUnit 2. Generated code for the runtime tests lands in `tests/Log0.Tests/obj/**/generated/` (`EmitCompilerGeneratedFiles` is on there).

## Architecture

A consumer writes `[Log] public T M(...) { ... }` (or puts `[Log]` on an interface method). Every direct call to it in the consuming project is redirected by a C# interceptor to a generated method that logs Start → calls `M` → logs Success, or Canceled/Error from an exception filter. Calls through delegates, reflection or assemblies without the generator are not logged. (A partial + `Core` form existed up to 0.1.0 and was removed.)

- **src/Log0** (`netstandard2.0;net8.0`, the only package): `LogAttribute`, `NoLogAttribute`, and the runtime the generated code calls:
  - `Log0Method`: one per generated method (static field). Owns levels, event ids (1 Start, 2 Success, 3 Canceled, 4 Error), parameter keys and the `Failed` exception filter (always returns false).
  - `Log0State`: the structured MEL state (`Method`, one key per parameter, `{OriginalFormat}`) and its text formatting.
  - `Log0Logging`: global factory + `AsyncLocal` override (`Use`) for tests. `Log0Logger`: per-class lazy logger, recreated when the current factory changes.
  - `AddLog0()` registers a hosted service that calls `Log0Logging.Configure` at host start; `UseLog0()` does it immediately.
  - Carries the generator as an analyzer asset (`PackSourceGenerator` target), like StronglyTypedIdentifier.
- **src/Log0.SourceGeneration** (`netstandard2.0`, not packable): `LogGenerator`: `ForAttributeWithMetadataName` on `Log0.LogAttribute` → `MethodAnalyzer.Analyze` reports each declaration's diagnostic once; a syntax provider over invocations → `CallAnalyzer` (call → `InterceptedCall`), collected and grouped into one file per declaring type → `LogEmitter` (source text). `MethodAnalyzer` also holds the shared symbol helpers.
  - The interceptor signature is rebuilt from symbols with fully qualified names (no usings needed), constraints repeated, parameter attributes and defaults dropped; `unsafe` added when a pointer appears.
  - Async shapes (`Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`) call the original synchronously (sync throws stay sync), fast-path already-successful tasks, otherwise await in a static local function. ValueTasks are read once and re-wrapped.
  - `CallAnalyzer` runs on every `InvocationExpressionSyntax`, keeps calls bound to a `[Log]` method (or one it overrides) and builds one interceptor `MethodModel` per method (`Attributes` = one `[InterceptsLocation]` per call site). Interceptors live in `internal static class <Type>_Log0Interceptors` under `Log0.Generated.<namespace>`; generic over the containing types' then the method's type parameters. `base.M()` calls are skipped. `src/Log0/build/Log0.props` (packed to `build/` and `buildTransitive/`) adds `Log0.Generated` to `InterceptorsNamespaces`; the repo's own test and sample projects import it by hand.
  - Logger: always the per-type shared `__log0Logger` field (a `Log0Logger` over the app's factory); fields on the user's class are never read.
  - Diagnostics `LOG004`–`LOG005` (`LOG001`–`LOG003` belonged to the removed partial form; don't reuse them) in `Diagnostics.cs`; new rules must also go in `AnalyzerReleases.Unshipped.md` (enforced) and the README table.
  - Models must stay value-equatable for incremental caching; use `EquatableArray<T>`.

## Tests

- `tests/Log0.Tests`: runtime behaviour, with the generator attached as an analyzer. `CapturingLoggerFactory` records entries; every test isolates itself with `using var _ = Log0Logging.Use( factory )`. Tests touching the global factory go in the `GlobalFactory` collection (parallelization disabled).
- `tests/Log0.SourceGeneration.Tests`: drives the generator by hand (`GeneratorHarness`), asserting diagnostics and that generated code compiles with **zero warnings** across awkward signatures and nullable contexts. Add a case there for every new signature shape.

## Conventions

- Code style: spaces inside parentheses, brackets and generic angle brackets (`Foo( x )`, `List< string >`), `#region` blocks, XML docs on public API. Generated code is exempt.
- `README.md` is packed into the NuGet package, so it is the user-facing documentation; keep it in sync with behaviour changes.

## Releasing

`.github/workflows/release.yml` packs, tests and runs the sample on every push and PR, and pushes to nuget.org only when a GitHub release is published (nuget.org trusted publishing, `NuGet/login` with user `sebdesalvador`, environment `nuget`; no stored API key). Versions come from MinVer: tag `vX.Y.Z` and it becomes the package version.
