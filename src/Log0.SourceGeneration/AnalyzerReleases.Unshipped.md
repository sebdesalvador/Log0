; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
LOG004 | Log0 | Error | The method shape is not supported: ref returns, iterators or async void.
LOG005 | Log0 | Error | Calls to a [Log] method cannot be intercepted, because generated code cannot reach it.
