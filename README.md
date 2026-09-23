# Log0

[![CI](https://github.com/sebdesalvador/Log0/actions/workflows/release.yml/badge.svg)](https://github.com/sebdesalvador/Log0/actions/workflows/release.yml)
[![NuGet](https://img.shields.io/nuget/v/Log0.svg)](https://www.nuget.org/packages/Log0)
[![Downloads](https://img.shields.io/nuget/dt/Log0.svg)](https://www.nuget.org/packages/Log0)
[![.NET](https://img.shields.io/badge/.NET-netstandard2.0%20%7C%208%2B-512BD4)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Method logging with zero boilerplate. Put `[Log]` on a method and a source generator writes the
logging around it.

```csharp
[Log]
public partial Task<Order> GetOrder(int id, CancellationToken ct);

private async Task<Order> GetOrderCore(int id, CancellationToken ct) => await _db.Find(id, ct);
```

```
info: Shop.OrderService[1] Start OrderService.GetOrder
info: Shop.OrderService[2] Success OrderService.GetOrder
```

It works at compile time, with no proxies, no reflection and no IL weaving. The generated code
is plain C# that you can read and step through in the debugger.

## Install

```bash
dotnet add package Log0
```

The package contains the attributes, a small runtime and the source generator.

## Quick start

**1. Connect Log0 to your app's logging** (once, in `Program.cs`):

```csharp
builder.Services.AddLog0();
```

**2. Mark a method.** Declare it `partial` without a body, and write the body in a method with the
same signature and a `Core` suffix:

```csharp
using Log0;

public partial class OrderService(IOrderRepository repository)
{
    [Log]
    public partial Task<Order> GetOrder(int id, CancellationToken ct);

    private Task<Order> GetOrderCore(int id, CancellationToken ct) => repository.Find(id, ct);
}
```

You don't need a constructor parameter, logger field or `ILogger<T>`. The class must be `partial`.

## What gets logged

| When | Message | Level | Event id |
|---|---|---|---|
| The method starts | `Start OrderService.GetOrder` | the attribute's level (default Information) | 1 `Start` |
| It returns, or its task completes | `Success OrderService.GetOrder` | the attribute's level | 2 `Success` |
| It throws `OperationCanceledException` | `Canceled OrderService.GetOrder` | Warning | 3 `Canceled` |
| It throws anything else | `Error OrderService.GetOrder` (with the exception) | Error | 4 `Error` |

- **The exception is never caught.** It is logged from an exception filter, before the stack
  unwinds, and keeps propagating unchanged with its original stack trace.
- **Async methods are logged when they finish.** For a method returning `Task`, `Task<T>`,
  `ValueTask` or `ValueTask<T>`, `Success` or `Error` is written when the task completes, not
  when the method returns it. If the implementation throws before it returns a task, the
  exception is still thrown synchronously, as it would be without Log0.
- **A `ValueTask` is read exactly once**, so `ValueTask`s backed by pooled `IValueTaskSource`s
  stay safe. Struct results are never boxed.
- **The logs are structured.** Each entry has a `Method` property (`OrderService.GetOrder`) and
  an `{OriginalFormat}` template, and appears under the class's category (`Shop.OrderService`,
  the same as `ILogger<OrderService>`). Filters in `appsettings.json` work as usual.

## Log levels

```csharp
[Log]                     // Start and Success at Information
[Log(LogLevel.Debug)]     // only written when Debug or lower is enabled for the class
[Log(LogLevel.Trace)]     // only written at Trace
```

`Canceled` is always a Warning and `Error` always an Error, whatever level the attribute sets,
so failures are never hidden by a verbose-only method.

## Parameter values

When **Debug or lower** is enabled for the class, `Start` also carries the parameter values, as
both text and structured properties:

```
dbug: Shop.OrderService[1] Start OrderService.GetOrder(id: 42)
```

```json
{ "Method": "OrderService.GetOrder", "id": 42, "{OriginalFormat}": "Start {Method}(id: {id})" }
```

- **Formatting:** values are formatted with the invariant culture. Collections are shown as
  `[1, 2, 3]`, capped at 20 items. `null` is shown as `null`.
- **Parameters that are left out:** `out` parameters (no value yet), `CancellationToken`, and
  values that can't be boxed (`Span<T>` and other ref structs, pointers).
- **Cost:** when Debug is off, no parameter is read or boxed.

## Keeping secrets out: `[NoLog]`

```csharp
// On a parameter: logged as ***
[Log]
public partial Task<Token> Login(string user, [NoLog] string password);

// On a type: every parameter of that type, in every method, is logged as ***
[NoLog]
public sealed record CardNumber(string Value);

// On a method: Start/Success/Canceled/Error are still written, but no parameter ever is
[Log, NoLog]
public partial void Rotate(string masterKey, string newKey);
```

```
Start AuthService.Login(user: ada, password: ***)
```

- **The value is never read:** the generated code writes the `***` constant instead, so no
  `ToString()` runs.
- **Inheritance:** a type counts as `[NoLog]` when it, a base class or any interface it
  implements carries the attribute. That includes `CardNumber?` and `CardNumber[]`.

## Where the logger comes from

1. **An `ILogger` field or property on the class**, including a non-private one on a base class.
   If the class already has one, Log0 uses it, so a logger injected per instance (for example a
   test double) receives the entries.
2. **Otherwise, the app's `ILoggerFactory`.** `AddLog0()` hands it to Log0 when the host starts,
   and each class gets a logger for its own category, created on first use.

Before the host starts, or in an app without a host, logging does nothing. To start earlier, or
to set the factory yourself:

```csharp
var app = builder.Build();
app.Services.UseLog0();                   // right away, before app.Run()

Log0Logging.Configure(loggerFactory);     // no dependency injection at all
```

## Testing

`Log0Logging.Use` replaces the factory for the current async flow only, so tests running in
parallel don't see each other's logs:

```csharp
[Fact]
public async Task Logs_the_lookup()
{
    var factory = new MyCapturingLoggerFactory();
    using var _ = Log0Logging.Use(factory);

    await new OrderService(repository).GetOrder(1, default);

    Assert.Contains("Success OrderService.GetOrder", factory.Messages);
}
```

## What is generated

For the `GetOrder` example above, abridged:

```csharp
private static readonly Log0Logger __log0Logger = new(typeof(OrderService));
private static readonly Log0Method __log0_GetOrder_0 = new("OrderService.GetOrder", LogLevel.Information, "id");

public partial Task<Order> GetOrder(int id, CancellationToken ct)
{
    ILogger __log0_logger = __log0Logger.Logger;
    Log0Method __log0_log = __log0_GetOrder_0;

    if (__log0_log.IsEnabled(__log0_logger))
        __log0_log.Start(__log0_logger, __log0_log.IncludesParameters(__log0_logger) ? new object?[] { id } : null);

    Task<Order> __log0_task;
    try { __log0_task = GetOrderCore(id, ct); }
    catch (Exception e) when (__log0_log.Failed(__log0_logger, e)) { throw; }

    if (__log0_task.Status == TaskStatus.RanToCompletion) { __log0_log.Success(__log0_logger); return __log0_task; }
    return __log0_Await(__log0_task, __log0_log, __log0_logger);

    static async Task<Order> __log0_Await(/* … */) { /* await, then Success */ }
}
```

To see the real output, build with `-p:EmitCompilerGeneratedFiles=true` and look under
`obj/**/generated/`. Your IDE can also show generated files directly.

## Supported methods

Log0 supports the following. Each case is covered by a test that checks the generated code
compiles without warnings.

- Instance and static methods, extension methods, and `virtual` or `override` methods.
- Generic methods with any constraints, and methods on generic types.
- Classes, structs, records, `record struct`s, `ref struct`s and nested types.
- `ref`, `in`, `ref readonly`, `out`, `params` and `scoped` parameters.
- Any nullable context, and any parameter names, including ones that look like the generated identifiers.

Not supported, reported as compile errors:

| Rule | Error |
|---|---|
| `LOG001` | `[Log]` is on a method that isn't a bodiless `partial` declaration |
| `LOG002` | There is no `…Core` method with the same parameters |
| `LOG003` | A type enclosing the method isn't `partial` |
| `LOG004` | Ref returns, iterators (`yield`), or an `async void` implementation |

## Why the `Core` method?

A source generator can add code, but it can't change code you wrote. So Log0 splits each method
into a public `partial` declaration that it implements and a `Core` method you implement. This
wraps the method itself, so every call is logged: calls through an interface, through dependency
injection, from other assemblies and through reflection.

Interceptors don't have this split, but they only rewrite direct calls within the same project.
Calls through an `IOrderService`, which covers most service code, would be silently missed.

## Sample

[`samples/Log0.Sample`](samples/Log0.Sample) is a console app with a host that logs success,
error, cancellation and masked values:

```bash
dotnet run --project samples/Log0.Sample              # Information
dotnet run --project samples/Log0.Sample -- --debug   # Debug: parameter values and [Log(LogLevel.Debug)] methods
```

## License

[MIT](LICENSE)
