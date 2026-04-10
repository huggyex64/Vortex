# Vortex

**A high-performance, type-safe event system for .NET with source-generated event domains.**

Vortex provides a robust publish/subscribe event infrastructure built around enum-keyed event managers, copy-on-write snapshots for lock-free invocation, priority-ordered handlers, and a Roslyn source generator that eliminates boilerplate. Originally developed for the [Prowl Game Engine](https://github.com/ProwlEngine/Prowl), Vortex is designed for latency-sensitive applications such as game engines, simulations, and real-time systems.

---

## Features

- **Source-Generated Event Domains** — Define events with `[EventDomain]` and `EventKey` fields. The generator produces an enum, an `EventManager<T>`, typed `Invoke` / `Subscribe` methods, `+=` / `-=` accessors, and global broadcast helpers — all at compile time.
- **Type-Safe Argument Contracts** — Annotate events with `[EventArgs(typeof(T))]` to enforce compile-time and runtime argument type matching. Mismatched handlers throw immediately on subscribe; mismatched invocations are caught and logged.
- **Priority-Ordered Handlers** — Every handler has an integer priority. Lower values run first, giving you deterministic execution order.
- **Copy-on-Write Snapshots** — Handler lists are rebuilt into flat arrays only when subscriptions change, never during invocation. The hot path is a simple array iteration with zero allocations.
- **Cancellable Events** — Implement `ICancellable` on your args struct to allow any handler to halt propagation for the remaining chain.
- **Sync & Async** — Full support for both `Action<T>` and `Func<T, Task>` handlers in the same priority chain. Use `InvokeAsync` to properly await async handlers.
- **Batched Mutations** — Wrap bulk subscribe/unsubscribe operations in `BeginBatch()` / `EndBatch()` to defer snapshot rebuilds until the batch completes.
- **Global Broadcast** — Mark domains or managers as `Global` to broadcast events across all active global instances with a single `GlobalInvoke` call.
- **IDisposable Subscriptions** — Every `Subscribe` call returns a disposable container. Call `Dispose()` or use a `using` statement to cleanly unsubscribe.
- **DEBUG Diagnostics** — In debug builds, handler registration captures caller info (`[CallerFilePath]`, `[CallerLineNumber]`). Slow handlers, type mismatches, and sync-over-async calls are logged with full source context.
- **AOT Compatible** — The library is marked `IsAotCompatible` and avoids patterns that break ahead-of-time compilation.

---

## Quick Start

### 1. Define an Event Domain

```csharp
using Vortex;

[EventDomain(Global = true)]
public static partial class GameEvents
{
    // Parameterless event
    private static readonly EventKey _OnGameStarted = new();

    // Typed event
    [EventArgs(typeof(ScoreChangedArgs))]
    private static readonly EventKey _OnScoreChanged = new();

    public readonly record struct ScoreChangedArgs(int OldScore, int NewScore);
}
```

The source generator produces:
- `GameEvents.EventTypes` enum with `OnGameStarted` and `OnScoreChanged` values
- `GameEvents.Manager` — the backing `EventManager<EventTypes>`
- `GameEvents.OnGameStarted` / `GameEvents.OnScoreChanged` accessor properties supporting `+=` / `-=`
- `InvokeOnGameStarted()`, `InvokeOnScoreChanged(args)`, and their `Async` variants
- `GlobalInvokeOnGameStarted()`, `GlobalInvokeOnScoreChanged(args)` for cross-manager broadcast
- `SubscribeOnGameStarted(handler, priority)` and `SubscribeOnScoreChanged(handler, priority)` returning `IDisposable` containers

### 2. Subscribe to Events

```csharp
// Simple += syntax (priority 0, no dispose handle)
GameEvents.OnScoreChanged += args =>
    Console.WriteLine($"Score: {args.OldScore} -> {args.NewScore}");

// Priority-controlled subscription with dispose handle
using var sub = GameEvents.SubscribeOnScoreChanged(
    args => Console.WriteLine("High-priority handler!"),
    priority: -10  // lower runs first
);
```

### 3. Invoke Events

```csharp
// Typed invocation
GameEvents.InvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(0, 100));

// Parameterless invocation
GameEvents.InvokeOnGameStarted();

// Broadcast to ALL global managers
GameEvents.GlobalInvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(100, 200));
```

### 4. Async Handlers

```csharp
// Subscribe an async handler
GameEvents.OnScoreChanged += async args =>
{
    await SaveScoreToCloudAsync(args.NewScore);
};

// Invoke with proper awaiting of async handlers
await GameEvents.InvokeOnScoreChangedAsync(new GameEvents.ScoreChangedArgs(0, 100));
```

---

## Instance Event Domains

For per-object events (e.g. component-level), omit `static` from the class:

```csharp
[EventDomain]
public partial class ActorEvents
{
    [EventArgs(typeof(DamageArgs))]
    private static readonly EventKey _OnDamaged = new();

    public readonly record struct DamageArgs(float Amount, string Source);
}

// Usage
var actor = new ActorEvents();
actor.OnDamaged += args => Console.WriteLine($"Took {args.Amount} damage from {args.Source}!");
actor.InvokeOnDamaged(new ActorEvents.DamageArgs(25f, "Fire"));

// Clean up when the object is no longer needed
actor.Manager.Dispose();
```

---

## Cancellable Events

Implement `ICancellable` on your args to allow handlers to stop propagation:

```csharp
public struct ValidateArgs : ICancellable
{
    public string Input;
    public bool Cancelled { get; set; }
}

// A high-priority validator can cancel the chain
GameEvents.SubscribeOnValidate(args =>
{
    if (string.IsNullOrEmpty(args.Input))
        args.Cancelled = true;  // subsequent handlers are skipped
}, priority: -100);
```

---

## Project Structure

| Project | Target | Description |
|---|---|---|
| `Vortex` | .NET 9 | Core event system library |
| `Vortex.Generators` | .NET Standard 2.0 | Roslyn incremental source generator for `[EventDomain]` |

---

## Documentation

| Document | Description |
|---|---|
| [Architecture](docs/ARCHITECTURE.md) | Internal design, copy-on-write strategy, snapshot management |
| [API Reference](docs/API-REFERENCE.md) | Complete public API documentation |
| [Source Generator](docs/SOURCE-GENERATOR.md) | How the `[EventDomain]` generator works and what it emits |
| [Advanced Usage](docs/ADVANCED-USAGE.md) | Async handlers, batching, diagnostics, global broadcast patterns |
| [Contributing](CONTRIBUTING.md) | Contribution guidelines |

---

## Requirements

- .NET 9.0+ (runtime library)
- .NET Standard 2.0 compatible compiler (source generator)
- C# 12+ recommended

## License

This project is part of the Prowl Game Engine and is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
