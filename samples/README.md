# Log0 sample

A console app with a host and an `OrderService` whose methods are marked `[Log]`. It shows:

| Call | What you see |
|---|---|
| `GetOrder(1)` | `Start` and `Success` for a `Task<Order>`, logged when the task completes |
| `IsValid(order)` | nothing at Information; `[Log(LogLevel.Debug)]` only shows with `--debug` |
| `Pay(1, card, cvv)` | `card` (a `[NoLog]` type) and `cvv` (a `[NoLog]` parameter) logged as `***` |
| `GetOrder(99)` | `Error` with the `KeyNotFoundException` and its original stack trace |
| `GetOrder(2)` with an expiring token | `Canceled` as a warning |

```bash
dotnet run --project samples/Log0.Sample              # Information
dotnet run --project samples/Log0.Sample -- --debug   # Debug: adds parameter values and Debug methods
```

Levels come from [`appsettings.json`](Log0.Sample/appsettings.json), exactly as for any `ILogger<T>`.
