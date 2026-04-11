# Vortex

### A high-performance, source-generated event system for .NET.

> **Zero-allocation invocation. Compile-time code generation. One attribute to define an entire event domain.**

Vortex is a **publish/subscribe event infrastructure** for .NET that turns a handful of field declarations into a fully wired, type-safe, priority-ordered event pipeline — with no manual wiring, no reflection, and no runtime overhead.

Built for **game engines**, **real-time simulations**, **plugin architectures**, and any .NET application where event dispatch sits on the hot path, Vortex combines the ergonomics of native C# events with the performance characteristics of hand-optimized code — generated entirely at compile time.

---

## Why Vortex?

| | Traditional C# Events | Generic Event Bus Libraries | **Vortex** |
|---|---|---|---|
| Type safety | ✅ Compile-time | ❌ Runtime casts | ✅ **Compile-time + runtime contracts** |
| Boilerplate | 🟡 Moderate | 🟡 Registration code | ✅ **Near-zero — source-generated** |
| Priority ordering | ❌ None | 🟡 Some libraries | ✅ **Built-in, deterministic** |
| Async support | ❌ Manual | 🟡 Varies | ✅ **First-class sync & async** |
| Allocation on invoke | 🟡 Delegate allocs | ❌ Boxing, dictionary lookups | ✅ **Zero — flat array iteration** |
| Cancellation | ❌ Roll your own | 🟡 Varies | ✅ **`ICancellable` interface** |
| Global broadcast | ❌ N/A | 🟡 Service locator | ✅ **One-line `GlobalInvoke`** |
| AOT / trimming | 🟡 Depends | ❌ Often broken | ✅ **Fully compatible** |
| Diagnostics | ❌ None | ❌ Rarely | ✅ **Caller-info tracing in DEBUG** |

Vortex is designed to give you the best of all three worlds: the type safety of native events, the flexibility of an event bus, and the performance of hand-rolled dispatch code — without writing any of it yourself.

---

## Features

- **Source-Generated Event Domains** — Apply `[EventDomain]` to a class and declare `EventKey` fields. The Roslyn incremental generator emits enums, typed `Invoke` / `Subscribe` methods, `+=` / `-=` accessors, and global broadcast helpers — all at compile time with full IntelliSense.
- **Type-Safe Argument Contracts** — Annotate events with `[EventArgs(typeof(T))]` to enforce argument types at both compile time and runtime. Mismatched handler signatures throw on subscribe; mismatched invocations are caught and logged immediately.
- **Priority-Ordered Handlers** — Every handler carries an integer priority. Lower values execute first, providing deterministic, predictable execution order across the entire handler chain.
- **Zero-Allocation Invocation** — Handler lists use a copy-on-write strategy: snapshots are rebuilt into flat arrays only when subscriptions change. The invocation hot path is a tight array loop with no allocations and no locks.
- **Cancellable Events** — Implement `ICancellable` on your args type and any handler can halt propagation. Well-suited for validation pipelines, permission checks, and interceptor patterns.
- **Sync & Async in One Pipeline** — Mix `Action<T>` and `Func<T, Task>` handlers in the same priority chain. Use `InvokeAsync` to properly `await` async handlers without fire-and-forget pitfalls.
- **Batched Mutations** — Wrap bulk subscribe/unsubscribe operations in `BeginBatch()` / `EndBatch()` to defer snapshot rebuilds. The snapshot is rebuilt once when the batch completes, not once per operation.
- **Global Broadcast** — Mark a domain as `Global` to broadcast events across all active instances with a single `GlobalInvoke` call — ideal for cross-system communication without tight coupling.
- **IDisposable Subscriptions** — Every `Subscribe` call returns a disposable handle. Use `using` blocks or explicit `Dispose()` for leak-free, deterministic cleanup.
- **Rich DEBUG Diagnostics** — In debug builds, handler registration captures `[CallerFilePath]` and `[CallerLineNumber]`. Slow handlers, type mismatches, and sync-over-async calls are logged with full source location context.
- **AOT & Trimming Ready** — Marked `IsAotCompatible` from the start. No reflection, no `Activator.CreateInstance` — compatible with Native AOT and trimmed deployments out of the box.

---

## Quick Start

### 1. Define an Event Domain

A complete event domain requires only a class attribute and field declarations:

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

From this declaration, the source generator produces the following at compile time:
- `GameEvents.EventTypes` enum with `OnGameStarted` and `OnScoreChanged` values
- `GameEvents.Manager` — the backing `EventManager<EventTypes>`
- `GameEvents.OnGameStarted` / `GameEvents.OnScoreChanged` accessor properties supporting `+=` / `-=`
- `InvokeOnGameStarted()`, `InvokeOnScoreChanged(args)`, and their `Async` variants
- `GlobalInvokeOnGameStarted()`, `GlobalInvokeOnScoreChanged(args)` for cross-manager broadcast
- `SubscribeOnGameStarted(handler, priority)` and `SubscribeOnScoreChanged(handler, priority)` returning `IDisposable` containers

All generated members have full IntelliSense and XML documentation. You declare the events; Vortex generates the infrastructure.

### 2. Subscribe to Events

The generated API supports familiar `+=` syntax as well as priority-controlled subscriptions with disposable handles:

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

Invocation methods are strongly typed and allocation-free:

```csharp
// Typed invocation
GameEvents.InvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(0, 100));

// Parameterless invocation
GameEvents.InvokeOnGameStarted();

// Broadcast across all global managers
GameEvents.GlobalInvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(100, 200));
```

### 4. Async Handlers

Async handlers are first-class — subscribe them the same way and use `InvokeAsync` to properly await the chain:

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

For per-object event pipelines — such as component-level signals in a game engine — omit the `static` modifier:

```csharp
[EventDomain]
public partial class ActorEvents
{
    [EventArgs(typeof(DamageArgs))]
    private static readonly EventKey _OnDamaged = new();

    public readonly record struct DamageArgs(float Amount, string Source);
}

// Every instance gets its own isolated event pipeline
var actor = new ActorEvents();
actor.OnDamaged += args => Console.WriteLine($"Took {args.Amount} damage from {args.Source}!");
actor.InvokeOnDamaged(new ActorEvents.DamageArgs(25f, "Fire"));

// Clean up when the object is no longer needed
actor.Manager.Dispose();
```

Static domains serve global systems; instance domains serve per-object signals. Both share the same API surface, the same performance characteristics, and the same source generation pipeline.

---

## Cancellable Events

Implement `ICancellable` on your args type to enable handler-driven cancellation — useful for validation pipelines, permission gates, and interceptor chains:

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

When `Cancelled` is set to `true`, all remaining handlers in the priority chain are skipped. Simple, predictable, and no exceptions to unwind.

---

## Project Structure

| Project | Target | Description |
|---|---|---|
| `Vortex` | .NET 9 | Core event system library |
| `Vortex.Generators` | .NET Standard 2.0 | Roslyn incremental source generator for `[EventDomain]` |

---

## Use Cases

Vortex is particularly well-suited for:

- **Game Engines & ECS** — Entity events, input systems, and game state transitions with zero-allocation dispatch.
- **Real-Time Simulations** — Priority-ordered, cancellable event chains for precise control over simulation tick processing.
- **Plugin Architectures** — Strongly typed event contracts that plugins can subscribe to without coupling to host internals.
- **Modular Monoliths** — Global broadcast for cross-module communication without a service bus or mediator.
- **UI Frameworks** — Reactive property changes, command routing, and view-model notifications with deterministic ordering.
- **Any latency-sensitive .NET application** where event dispatch is on the hot path and allocations matter.

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

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

<p align="center">
  <b>Vortex</b> — Declare your events. The compiler handles the rest.
</p>
