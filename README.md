# ⚡ Vortex

### The last event system your .NET project will ever need.

> **Zero-allocation invocation. Compile-time codegen. One attribute and you're done.**

Vortex is a **high-performance, source-generated publish/subscribe event system** for .NET that turns a few lines of declaration into a fully wired, type-safe, priority-ordered event infrastructure — no manual wiring, no reflection, no runtime overhead.

Whether you're building a **game engine**, a **real-time simulation**, a **modular plugin architecture**, or just want events that *don't suck*, Vortex delivers the fastest, cleanest event pipeline available on .NET today.

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

**Stop wiring events by hand.** Let the compiler do it for you — faster, safer, and with zero boilerplate.

---

## ✨ Features

- **🔧 Source-Generated Event Domains** — Slap `[EventDomain]` on a class, declare your `EventKey` fields, and the Roslyn generator does the rest: enums, typed `Invoke` / `Subscribe` methods, `+=` / `-=` accessors, and global broadcast helpers — all emitted at compile time with full IntelliSense support.
- **🛡️ Type-Safe Argument Contracts** — Annotate events with `[EventArgs(typeof(T))]` and never worry about type mismatches again. Wrong handler signature? Instant exception on subscribe. Wrong invocation type? Caught and logged before it can do damage.
- **📊 Priority-Ordered Handlers** — Every handler carries an integer priority. Lower values execute first. No guessing, no race conditions — just clean, deterministic execution order you can reason about.
- **🚀 Zero-Allocation Invocation** — Handler lists use a copy-on-write strategy: snapshots are rebuilt into flat arrays *only* when subscriptions change. The hot path is a tight array loop with **zero allocations and zero locks**.
- **🛑 Cancellable Events** — Implement `ICancellable` on your args and any handler can halt the chain. Perfect for validation pipelines, permission checks, and interceptor patterns.
- **⚡ Sync & Async in One Pipeline** — Mix `Action<T>` and `Func<T, Task>` handlers freely in the same priority chain. Call `InvokeAsync` when you need to properly `await` the async ones.
- **📦 Batched Mutations** — Subscribing hundreds of handlers at startup? Wrap it in `BeginBatch()` / `EndBatch()` and the snapshot rebuilds *once* instead of N times.
- **🌐 Global Broadcast** — Mark a domain as `Global` and fire events across every active instance with a single `GlobalInvoke` call. Ideal for cross-system communication without tight coupling.
- **♻️ IDisposable Subscriptions** — Every `Subscribe` returns a disposable handle. Use `using` blocks or explicit `Dispose()` for leak-free, deterministic cleanup.
- **🔍 Rich DEBUG Diagnostics** — In debug builds, every handler registration captures `[CallerFilePath]` and `[CallerLineNumber]`. Slow handlers, type mismatches, and sync-over-async calls are flagged with full source context so you find bugs *before* your players do.
- **💎 AOT & Trimming Ready** — Marked `IsAotCompatible` from day one. No reflection, no `Activator.CreateInstance`, no surprises when you publish with Native AOT or trimmed deployments.

---

## 🚀 Quick Start — Up and Running in 60 Seconds

### 1. Define an Event Domain

This is all you write:

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

That's it. **The source generator takes care of everything else.** At compile time, Vortex produces:
- `GameEvents.EventTypes` enum with `OnGameStarted` and `OnScoreChanged` values
- `GameEvents.Manager` — the backing `EventManager<EventTypes>`
- `GameEvents.OnGameStarted` / `GameEvents.OnScoreChanged` accessor properties supporting `+=` / `-=`
- `InvokeOnGameStarted()`, `InvokeOnScoreChanged(args)`, and their `Async` variants
- `GlobalInvokeOnGameStarted()`, `GlobalInvokeOnScoreChanged(args)` for cross-manager broadcast
- `SubscribeOnGameStarted(handler, priority)` and `SubscribeOnScoreChanged(handler, priority)` returning `IDisposable` containers

> 💡 **You declare the intent. Vortex generates the infrastructure.** Full IntelliSense, full type safety, zero hand-written plumbing.

### 2. Subscribe to Events

Subscribing feels native — just use `+=` like you would with any C# event:

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

Firing events is a one-liner — typed, safe, and allocation-free:

```csharp
// Typed invocation
GameEvents.InvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(0, 100));

// Parameterless invocation
GameEvents.InvokeOnGameStarted();

// Broadcast to ALL global managers — one call, every listener, everywhere
GameEvents.GlobalInvokeOnScoreChanged(new GameEvents.ScoreChangedArgs(100, 200));
```

### 4. Async Handlers

Async is a first-class citizen — no workarounds, no fire-and-forget footguns:

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

## 🎯 Instance Event Domains

Need per-object events — like component-level signals in a game engine? Just drop the `static` keyword:

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

Static domains for global systems, instance domains for per-object signals — same API, same performance, same source generation.

---

## 🛑 Cancellable Events

Build validation pipelines, permission gates, and interceptor patterns with zero effort. Just implement `ICancellable`:

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

## 🏗️ Project Structure

| Project | Target | Description |
|---|---|---|
| `Vortex` | .NET 9 | Core event system library |
| `Vortex.Generators` | .NET Standard 2.0 | Roslyn incremental source generator for `[EventDomain]` |

---

## 🎓 Perfect For

- **Game Engines & ECS** — Wire up entity events, input systems, and game state transitions with zero-allocation dispatch.
- **Real-Time Simulations** — Priority-ordered, cancellable event chains give you precise control over simulation tick processing.
- **Plugin Architectures** — Let plugins subscribe to well-typed events without coupling to the host application's internals.
- **Modular Monoliths & Microservices** — Use global broadcast for cross-module communication without a service bus.
- **UI Frameworks** — Reactive property changes, command routing, and view-model notifications with deterministic ordering.
- **Any latency-sensitive .NET application** where events are on the hot path.

---

## 📚 Documentation

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
  <b>Vortex</b> — Declare your events. Let the compiler do the rest. ⚡
</p>
