# Advanced Usage

This document covers advanced patterns and features of the Vortex event system, including async handlers, batching, cancellation, global broadcasting, diagnostics, and collection-based invocation.

---

## Table of Contents

- [Priority Ordering](#priority-ordering)
- [Disposable Subscriptions](#disposable-subscriptions)
- [Async Handlers](#async-handlers)
- [Cancellable Events](#cancellable-events)
- [Batched Mutations](#batched-mutations)
- [Global Broadcasting](#global-broadcasting)
- [Collection-Based Invocation](#collection-based-invocation)
- [Enabling / Disabling](#enabling--disabling)
- [Diagnostics Setup](#diagnostics-setup)
- [DEBUG Diagnostics](#debug-diagnostics)
- [Manual EventManager Usage](#manual-eventmanager-usage)
- [Best Practices](#best-practices)

---

## Priority Ordering

Every handler has an integer priority. **Lower values run first.** The default priority is `0`.

```csharp
// Runs third (priority 10)
GameEvents.SubscribeOnScoreChanged(args => Console.WriteLine("C"), priority: 10);

// Runs first (priority -5)
GameEvents.SubscribeOnScoreChanged(args => Console.WriteLine("A"), priority: -5);

// Runs second (priority 0, default)
GameEvents.SubscribeOnScoreChanged(args => Console.WriteLine("B"));

GameEvents.InvokeOnScoreChanged(args);
// Output: A, B, C
```

Handlers with the same priority execute in registration order within that priority bucket.

---

## Disposable Subscriptions

The `Subscribe*` methods return a container that implements `IDisposable`. Calling `Dispose()` unsubscribes the handler.

```csharp
// Manual dispose
var sub = GameEvents.SubscribeOnScoreChanged(args => { ... });
// ... later
sub.Dispose();

// Using statement — auto-unsubscribes at end of scope
using var sub = GameEvents.SubscribeOnScoreChanged(args => { ... });
```

You can also disable a handler without unsubscribing:

```csharp
var sub = GameEvents.SubscribeOnScoreChanged(args => { ... });
sub.Disable();  // handler is skipped during invocation
sub.Enable();   // handler is active again
```

---

## Async Handlers

Vortex supports both synchronous and asynchronous handlers in the same priority chain.

### Subscribing Async Handlers

```csharp
// Via += syntax
GameEvents.OnScoreChanged += async args =>
{
    await SaveToCloudAsync(args.NewScore);
};

// Via Subscribe method (returns disposable)
using var sub = GameEvents.SubscribeOnScoreChangedAsync(
    async args => await NotifyPlayersAsync(args),
    priority: 5
);
```

### Invoking with Async Support

```csharp
// Synchronous invoke — async handlers fire but are NOT awaited
// DEBUG builds log a warning for sync-over-async
GameEvents.InvokeOnScoreChanged(args);

// Async invoke — properly awaits async handlers sequentially
await GameEvents.InvokeOnScoreChangedAsync(args);
```

When using `InvokeAsync`:
- Synchronous handlers are called inline.
- Async handlers (`IAsyncInvocable<TArgs>`) are awaited before proceeding to the next handler.
- Priority ordering and cancellation semantics are identical to synchronous invocation.

> **Recommendation:** Use `InvokeAsync` for editor/tool events that perform I/O. For game-loop hot paths, prefer synchronous `Invoke`.

---

## Cancellable Events

Implement `ICancellable` on your args type to allow handlers to halt propagation:

```csharp
public struct InputEventArgs : ICancellable
{
    public string Key;
    public bool Cancelled { get; set; }
}

[EventDomain]
public static partial class InputEvents
{
    [EventArgs(typeof(InputEventArgs))]
    private static readonly EventKey _OnKeyPressed = new();
}
```

A high-priority handler can cancel the chain:

```csharp
InputEvents.SubscribeOnKeyPressed(args =>
{
    if (args.Key == "Escape" && IsMenuOpen)
    {
        args.Cancelled = true;  // lower-priority handlers are skipped
        CloseMenu();
    }
}, priority: -100);

InputEvents.SubscribeOnKeyPressed(args =>
{
    // This handler is skipped if a higher-priority handler cancelled
    ProcessGameInput(args.Key);
}, priority: 0);
```

**Performance note:** The cancellation check uses a JIT-time constant (`CancellableCheck<TArgs>.IsCancellable`). When `TArgs` does not implement `ICancellable`, the JIT eliminates the check entirely — zero overhead for non-cancellable events.

---

## Batched Mutations

When subscribing or unsubscribing many handlers at once, wrap the operations in a batch to avoid rebuilding the internal COW snapshot after each mutation:

```csharp
var manager = GameEvents.Manager;
manager.BeginBatch();
try
{
    for (int i = 0; i < 100; i++)
        GameEvents.SubscribeOnScoreChanged(Handler);
}
finally
{
    manager.EndBatch();  // single snapshot rebuild here
}
```

Batches can be nested. The snapshot is rebuilt only when the outermost `EndBatch()` is called, and only if mutations actually occurred.

---

## Global Broadcasting

### Static Domains

Mark the domain as global to participate in cross-manager broadcast:

```csharp
[EventDomain(Global = true)]
public static partial class EngineEvents
{
    private static readonly EventKey _OnShutdown = new();
}

// This invokes OnShutdown on ALL global EventManager<EngineEvents.EventTypes> instances
EngineEvents.GlobalInvokeOnShutdown();
```

### Instance Domains with Global Managers

Instance domains can also participate in global broadcast by setting `Manager.Global = true`:

```csharp
[EventDomain]
public partial class SceneEvents
{
    private static readonly EventKey _OnLoaded = new();
}

var scene = new SceneEvents();
scene.Manager.Global = true;  // now participates in global invoke

// Broadcast to all global SceneEvents managers
SceneEvents.GlobalInvokeOnLoaded();
```

### How It Works

`EventManager<T>` maintains a static copy-on-write array of all global instances. `GlobalInvokeEvent` iterates this snapshot and invokes on each enabled manager. No locking occurs during the iteration.

---

## Collection-Based Invocation

When multiple objects implement `IEventManagerHolder<T>`, you can invoke events across all of them:

```csharp
[EventDomain]
public partial class ActorEvents : IEventManagerHolder<ActorEvents.EventTypes>
{
    [EventArgs(typeof(DamageArgs))]
    private static readonly EventKey _OnDamaged = new();

    // IEventManagerHolder implementation
    EventManager<EventTypes> IEventManagerHolder<EventTypes>.EventManager => Manager;
}

// Invoke across a collection
ActorEvents[] actors = GetAllActors();
actors.InvokeEvents(ActorEvents.EventTypes.OnDamaged, new DamageArgs(50));
```

Extension methods are provided for both `T[]` and `List<T>`.

---

## Enabling / Disabling

### Manager Level

```csharp
GameEvents.Manager.Enabled = false;  // all invocations become no-ops
GameEvents.Manager.Enabled = true;
```

### Event Level

```csharp
GameEvents.Manager.DisableEvent(GameEvents.EventTypes.OnScoreChanged);
GameEvents.Manager.EnableEvent(GameEvents.EventTypes.OnScoreChanged);
```

### Handler Level

```csharp
var sub = GameEvents.SubscribeOnScoreChanged(args => { ... });
sub.Disable();  // this specific handler is skipped
sub.Enable();
```

---

## Diagnostics Setup

Wire up logging during application startup:

```csharp
// Route through your logging framework
EventSystemDiagnostics.LogWarning = msg => MyLogger.Warn(msg);
EventSystemDiagnostics.LogError = msg => MyLogger.Error(msg);
```

### Messages Logged

| Level | Condition | Example |
|---|---|---|
| Error | Type mismatch on invoke | `Type mismatch on GameEvents.OnScore: invoked with 'String' but declares 'ScoreArgs'` |
| Warning | Slow handler (DEBUG) | `Slow handler on GameEvents.OnScore: 350.12ms (threshold 200.0ms)` |
| Warning | Sync-over-async (DEBUG) | `Async handler invoked synchronously. Use InvokeAsync.` |
| Warning | Type mismatch handler skipped (DEBUG) | `Handler registered for 'OtherArgs' but invoked with 'ScoreArgs'. Skipped.` |
| Warning | Undisposed manager (finalizer) | `EventManager<EventTypes> was not disposed before finalization.` |

---

## DEBUG Diagnostics

These features are only active in `DEBUG` builds (compiled with `#if DEBUG`):

### Slow Handler Detection

```csharp
// Configure the threshold (default: 200ms)
Event<GameEvents.EventTypes>.SlowHandlerThresholdMs = 100.0;

// Set to 0 to disable
Event<GameEvents.EventTypes>.SlowHandlerThresholdMs = 0;
```

When a handler exceeds the threshold, a warning is logged with the handler's source location.

### Caller Info Capture

Every `Subscribe` call automatically captures the caller's file path, line number, and member name. This information appears in diagnostic messages:

```
[EventSystem] Slow handler on GameEvents.OnScore: 350.12ms (threshold 200.0ms).
Handler: PlayerController.cs:42 (OnEnable)
```

### Sync-Over-Async Warning

When an `AsyncEventDelegateContainer` is invoked through the synchronous `Invoke` path (i.e., the caller used `InvokeEvent` instead of `InvokeEventAsync`), a warning is logged.

---

## Manual EventManager Usage

You can use `EventManager<T>` directly without the source generator, using your own enum:

```csharp
public enum MyEvents
{
    [EventArgs(typeof(string))]
    OnMessage,

    OnTick,
}

// Create a manager
using var manager = new EventManager<MyEvents>();

// Subscribe
var sub = manager.AddNewDelegate<string>(MyEvents.OnMessage, msg =>
    Console.WriteLine($"Received: {msg}"));

// Invoke
manager.InvokeEvent(MyEvents.OnMessage, "Hello!");

// Unsubscribe
sub.Dispose();
```

This approach gives full control but requires manually managing the manager lifecycle and lacks the convenience of generated `Invoke` / `Subscribe` methods.

---

## Best Practices

1. **Prefer `[EventDomain]` over manual `EventManager`** — The source generator eliminates boilerplate and provides compile-time type safety.

2. **Always annotate with `[EventArgs]`** — Without it, any `TArgs` is accepted (opt-in safety model), losing type checking.

3. **Use `Dispose()` on subscriptions** — Prevents memory leaks from long-lived handlers referencing short-lived objects. Prefer `using var` when the subscription scope matches a method.

4. **Use batching for bulk operations** — When subscribing/unsubscribing many handlers at once, wrap in `BeginBatch()` / `EndBatch()` to avoid unnecessary snapshot rebuilds.

5. **Use `InvokeAsync` when async handlers are expected** — Synchronous `Invoke` on async handlers fires them without awaiting, which can lead to unobserved exceptions.

6. **Keep handlers fast on the game loop** — The event system is designed for low-latency invocation. Reserve heavy I/O for async-invoked editor/tool events.

7. **Wire up `EventSystemDiagnostics` early** — Ensures you see type-mismatch errors and slow handler warnings during development.

8. **Dispose instance domain managers** — Instance (non-static) domains create per-object managers that register in the static tracking list. Always call `Manager.Dispose()` when the owning object is destroyed.

9. **Use `ICancellable` sparingly** — Cancellation adds a conditional branch per handler (though JIT-eliminated for non-cancellable types). Reserve it for events where propagation control is semantically meaningful.

10. **Use struct args** — Value-type args avoid heap allocation on every invocation. Use `readonly record struct` for concise, immutable payloads.
