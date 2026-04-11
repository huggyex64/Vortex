# Vortex

### A high-performance, source-generated event system for .NET.

> **Zero-allocation invocation. Compile-time code generation. One attribute to define an entire event domain.**

Vortex is a **publish/subscribe event infrastructure** for .NET built around three core principles: the compiler should write your boilerplate, the hot path should never allocate, and type mismatches should be impossible to ship.

A single `[EventDomain]` attribute on a `partial class` triggers a Roslyn incremental source generator that emits a complete, strongly typed event API — enum definitions, `EventManager<T>` backing stores, typed `Invoke`/`Subscribe` methods with `AggressiveInlining`, `+=`/`-=` operator accessors, `IDisposable` subscription handles, and global broadcast helpers — all at compile time, all with full IntelliSense and XML documentation.

The runtime invocation path is a flat, priority-sorted `EventDelegateContainer<T, TArgs>[]` iteration with no dictionary lookups, no delegate list copies, and no per-invocation allocations. Subscriptions mutate a copy-on-write snapshot under a lock; invocations read a snapshot reference with no synchronization. The two paths never contend.

---

## Why Vortex?

Most .NET event solutions force you to choose between ergonomics, performance, and safety. Vortex is designed so you don't have to.

| | Traditional C# Events | Generic Event Bus Libraries | **Vortex** |
|---|---|---|---|
| Type safety | ✅ Compile-time | ❌ Runtime casts | ✅ **Compile-time + `[EventArgs]` runtime contracts** |
| Boilerplate | 🟡 Moderate | 🟡 Registration code | ✅ **Near-zero — source-generated** |
| Priority ordering | ❌ None | 🟡 Some libraries | ✅ **Built-in, deterministic integer priority** |
| Async support | ❌ Manual | 🟡 Varies | ✅ **Mixed sync/async in one priority chain** |
| Allocation on invoke | 🟡 Delegate allocs | ❌ Boxing, dictionary lookups | ✅ **Zero — per-`TArgs` typed array iteration** |
| Cancellation | ❌ Roll your own | 🟡 Varies | ✅ **`ICancellable` with JIT-time type check** |
| Global broadcast | ❌ N/A | 🟡 Service locator | ✅ **Copy-on-write `GlobalInvoke`** |
| AOT / trimming | 🟡 Depends | ❌ Often broken | ✅ **`IsAotCompatible`, no reflection in hot path** |
| Diagnostics | ❌ None | ❌ Rarely | ✅ **`[CallerFilePath]` + slow-handler detection** |

The result: the type safety of native C# events, the flexibility of a pub/sub bus, and the performance of hand-rolled dispatch — generated for you at compile time.

---

## Features

- **Source-Generated Event Domains** — Apply `[EventDomain]` to a `partial class` and declare `EventKey` fields. The Roslyn incremental generator (`IIncrementalGenerator`) emits an enum, an `EventManager<T>`, per-event typed `Invoke`/`Subscribe` methods marked `[AggressiveInlining]`, `EventAccessor<T>` properties supporting `+=`/`-=`, and global broadcast helpers. The generator uses `ForAttributeWithMetadataName` for efficient incremental caching — it only re-runs when your event declarations actually change.
- **Type-Safe Argument Contracts** — Annotate events with `[EventArgs(typeof(T))]`. The `EventArgsContract<T>` static class builds a per-enum-value type map at first access and validates every `Subscribe` call at registration time (throwing `InvalidOperationException` with an actionable message) and every `Invoke` call at dispatch time (logging via `EventSystemDiagnostics` and short-circuiting). Mismatched handlers never silently fire.
- **Priority-Ordered Handlers** — Delegates are stored in priority-keyed buckets (`ConcurrentDictionary<int, List<...>>`) with sorted key iteration. Lower integers execute first. The ordering is deterministic and fully controlled by the caller.
- **Zero-Allocation Invocation** — Each `Event<T>` maintains per-`TArgs` typed snapshot arrays (`EventDelegateContainer<T, TArgs>[]`) alongside a full untyped snapshot. `Invoke<TArgs>` retrieves the matching typed array with a single `ConcurrentDictionary` lookup and one reference cast — no per-element type checks, no boxing, no delegate list copying. Snapshots are rebuilt only on `Add`/`Remove`, never on `Invoke`.
- **Cancellable Events** — Implement `ICancellable` on your args type. Vortex uses a JIT-time static class (`CancellableCheck<TArgs>.IsCancellable`) to determine at compile time whether the cancellation check should run — value-type args are never boxed just to test the interface.
- **Sync & Async in One Pipeline** — Synchronous (`Action<TArgs>`) and asynchronous (`Func<TArgs, Task>`) handlers coexist in the same priority chain via `EventDelegateContainer<T, TArgs>` and `AsyncEventDelegateContainer<T, TArgs>`. `InvokeAsync` detects `IAsyncInvocable<TArgs>` and awaits; synchronous `Invoke` fires async handlers with a logged warning in DEBUG builds so sync-over-async mistakes are caught immediately.
- **Batched Mutations** — `BeginBatch()`/`EndBatch()` increment a depth counter on each `Event<T>`. While batched, `Add`/`Remove` defer snapshot rebuilds. When the outermost `EndBatch()` completes, the snapshot is rebuilt exactly once. Batches are nestable.
- **Global Broadcast** — `EventManager<T>` maintains a static copy-on-write `s_globalSnapshot` array of all managers with `Global = true`. `GlobalInvokeEvent` iterates the snapshot with a simple `for` loop — no locks, no allocations, no service locator.
- **IDisposable Subscriptions** — `EventDelegateContainer<T>` implements `IDisposable`. `Dispose()` calls `Event.Remove(this)`, enabling `using` blocks for deterministic cleanup. The dispose-guard flag prevents double-removal.
- **Rich DEBUG Diagnostics** — In debug builds, every `AddNewDelegate` overload captures `[CallerFilePath]`, `[CallerLineNumber]`, and `[CallerMemberName]` into the container. Slow handlers are detected via `Stopwatch` against a configurable `SlowHandlerThresholdMs` (default 200ms). Diagnostics are surfaced through `EventSystemDiagnostics.LogWarning`/`LogError` — assign your engine's logger at startup.
- **AOT & Trimming Ready** — The project is marked `<IsAotCompatible>true</IsAotCompatible>`. The hot path uses no reflection. `EventArgsContract<T>` uses `Enum.GetValues<T>()` and `GetCustomAttribute` once at static init (cold path only). `Event<T>.CreateArrayBuilder` uses `MakeGenericMethod` once per `TArgs` type and caches the resulting delegate. No `Activator.CreateInstance`, no `dynamic`, no `Expression.Compile`.

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

## Under the Hood

Vortex's performance comes from architectural decisions, not micro-optimization tricks. Here's how the pieces fit together:

**Invocation path (hot):**
```
Invoke<TArgs>(args)
  → _typedSnapshots[typeof(TArgs)]          // single ConcurrentDictionary lookup
  → (EventDelegateContainer<T, TArgs>[])obj  // one reference cast, no per-element checks
  → for loop: container.Invoke(args)         // direct virtual call, no boxing
  → ICancellable check (JIT-eliminated for non-cancellable TArgs)
```

**Subscription path (cold):**
```
AddNewDelegate<TArgs>(eventType, handler, priority)
  → EventArgsContract<T>.IsValid<TArgs>()    // cached type map, throws on mismatch
  → lock (_lock) { Add to priority bucket }
  → RebuildSnapshot()                        // flat array rebuild
  → RebuildTypedSnapshots()                  // per-TArgs typed arrays via cached MakeGenericMethod delegate
```

Key design decisions:
- **Per-`TArgs` typed snapshots** — `Invoke<TArgs>` never iterates handlers of a different type. The typed array is retrieved in O(1) and iterated without any runtime type checks.
- **`CancellableCheck<TArgs>` static class** — The JIT evaluates `typeof(ICancellable).IsAssignableFrom(typeof(TArgs))` once per closed generic. For non-cancellable types, the branch is eliminated entirely.
- **`ParameterlessEventDelegateContainer<T>`** — Parameterless events store `Action` directly instead of wrapping in `Action<Unit>`, avoiding a closure allocation on every subscribe.
- **Cached array builders** — `Event<T>.CreateArrayBuilder` uses `MakeGenericMethod` once per `TArgs` type, then caches the resulting `Func<List<...>, object>` delegate. Subsequent snapshot rebuilds for that type pay zero reflection cost.
- **Copy-on-write global snapshot** — `EventManager<T>.s_globalSnapshot` is a plain array replaced atomically under a lock. `GlobalInvokeEvent` reads the reference without synchronization and iterates a stable snapshot.

---

## Project Structure

| Project | Target | Description |
|---|---|---|
| `Vortex` | .NET 9 | Core event system library |
| `Vortex.Generators` | .NET Standard 2.0 | Roslyn incremental source generator for `[EventDomain]` |

---

## Use Cases

Vortex is designed for scenarios where event dispatch is on the critical path:

- **Game Engines & ECS** — Entity lifecycle events, input dispatch, game state transitions. The zero-allocation invoke path means no GC pressure during gameplay.
- **Real-Time Simulations** — Priority-ordered handler chains give deterministic control over processing order within a simulation tick.
- **Plugin Architectures** — Plugins subscribe to strongly typed event contracts without coupling to host internals. `IDisposable` handles make cleanup straightforward when plugins unload.
- **Modular Monoliths** — Global broadcast enables cross-module communication through shared event domains without introducing a service bus or mediator dependency.
- **UI Frameworks** — Property-change notifications, command routing, and view-model events with deterministic ordering and cancellation support.

---

## Design Philosophy

1. **The compiler should do the work.** If a method signature, enum value, or type contract can be generated, it should be. Hand-written event boilerplate is a maintenance liability.
2. **The hot path is sacred.** `Invoke` must never allocate, never lock, and never perform type checks that the JIT can't eliminate. All bookkeeping happens on subscribe.
3. **Wrong code should fail loudly.** Type mismatches throw `InvalidOperationException` on subscribe and log detailed error messages on invoke — with source file and line number in DEBUG builds.
4. **Async is not an afterthought.** Synchronous and asynchronous handlers coexist in the same priority chain. Accidentally calling sync `Invoke` on an async handler triggers a visible warning, not a silent fire-and-forget.

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
  <b>Vortex</b> — Declare your events. The compiler writes the infrastructure. The hot path stays clean.
</p>
