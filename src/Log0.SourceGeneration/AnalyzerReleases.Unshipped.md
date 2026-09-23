; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
LOG001 | Log0 | Error | A [Log] method must be a partial method declaration without a body.
LOG002 | Log0 | Error | A [Log] method needs an implementation method with the same parameters and a Core suffix.
LOG003 | Log0 | Error | Every type containing a [Log] method must be partial.
LOG004 | Log0 | Error | The method shape is not supported: ref returns, iterators or async void.
