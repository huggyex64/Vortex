# Architecture

This document describes the internal design of Vortex, covering the core data structures, concurrency model, snapshot strategy, and the relationship between key components.

---

## High-Level Overview

```
┌─────────────────────────────────────────────────────────┐
│                    [EventDomain]                        │
│  Source-generated partial class                         │
│  ┌────────────┐  ┌──────────────────┐  ┌────────────┐  │
│  │ EventTypes  │  │ EventAccessor<T> │  │ Invoke /   │  │
│  │   (enum)    │  │ += / -= / .Invoke│  │ Subscribe  │  │
│  └─────┬──────┘  └────────┬─────────┘  └──────┬─────┘  │
│        │                  │                    │        │
│        └──────────┬───────┴────────────────────┘        │
│                   ▼                                     │
│          EventManager<T>                                │
│          ┌──────────────────────────┐                   │
│          │ ConcurrentDictionary     │                   │
│          │   T → Event<T>           │                   │
│          │                          │                   │
│          │ Global / Enabled flags   │                   │
│          │ Static instance tracking │                   │
│          └────────────┬─────────────┘                   │
│                       ▼                                 │
│              Event<T>  (per enum value)                 │
│              ┌──────────────────────────────────┐       │
│              │ Priority buckets (ConcurrentDict) │      │
│              │ COW snapshot: flat sorted array    │      │
│              │ Per-TArgs typed snapshots          │      │
│              │ Batch depth tracking               │      │
│              └──────────────────────────────────┘       │
│                       ▼                                 │
│       EventDelegateContainer<T, TArgs>                  │
│       ┌───────────────────────────────────┐             │
│       │ Action<TArgs> / Func<TArgs, Task> │             │
│       │ Priority, Enabled, IDisposable    │             │
│       │ DEBUG: CallerInfo                 │             │
│       └───────────────────────────────────┘             │
└─────────────────────────────────────────────────────────┘
```

---

## Core Components

### EventManager\<T\>

The top-level container, parameterized on an `enum` type `T`. Each enum value maps to one `Event<T>` instance, created lazily on first access via `ConcurrentDictionary.GetOrAdd`.

**Key responsibilities:**
- Owns the `ConcurrentDictionary<T, Event<T>>` mapping enum values to events.
- Maintains a static list of all `EventManager<T>` instances for the same `T`, enabling global broadcast.
- Provides `AddNewDelegate`, `RemoveDelegate`, `InvokeEvent`, and their async variants.
- Enforces `[EventArgs]` type contracts on both subscribe and invoke.
- Supports `Dispose()` for cleanup, removing itself from the static instance list.

**Static instance tracking:**
```
s_instances         — List<EventManager<T>>      (mutable, under lock)
s_instancesSnapshot — EventManager<T>[]          (COW, rebuilt on add/remove)
s_globalSnapshot    — EventManager<T>[]          (COW, only global instances)
```

All mutations to the instance list are performed under `s_instancesLock`. After each mutation, both snapshot arrays are rebuilt as fresh allocations so that readers (e.g. `GlobalInvokeEvent`) never need to lock.

### Event\<T\>

Represents a single event slot identified by one enum value. Holds the handler list and manages the copy-on-write snapshot.

**Priority buckets:**
```
_eventDelegates: ConcurrentDictionary<int, List<EventDelegateContainer<T>>>
_sortedKeys:     List<int>  (maintained sorted on mutation)
```

Handlers are stored in buckets keyed by priority. `_sortedKeys` maintains the sorted order of priority levels so that rebuilding the flat snapshot is a simple sequential pass.

**Copy-on-write snapshots:**
```
_cachedSnapshot: EventDelegateContainer<T>[]     — full snapshot (DEBUG diagnostics)
_typedSnapshots: ConcurrentDictionary<Type, object>  — per-TArgs typed arrays
```

On every `Add` / `Remove` (outside of batching), `RebuildSnapshot()` creates a new flat array from the priority-sorted buckets. `RebuildTypedSnapshots()` then groups containers by their `ArgsType` property and builds strongly-typed arrays (`EventDelegateContainer<T, TArgs>[]`) via cached generic delegates — avoiding `Array.CreateInstance` and per-element boxing.

### EventDelegateContainer\<T\> / EventDelegateContainer\<T, TArgs\>

The non-generic base provides the priority, enabled flag, event type, `IDisposable` for self-unsubscription, and `ArgsType` for snapshot grouping. The generic subclass wraps an `Action<TArgs>` and provides `Invoke(TArgs)`.

**Specializations:**
- `ParameterlessEventDelegateContainer<T>` — stores `Action` directly, avoiding closure allocation over `Action<Unit>`.
- `AsyncEventDelegateContainer<T, TArgs>` — wraps `Func<TArgs, Task>`, implements `IAsyncInvocable<TArgs>`.
- `ParameterlessAsyncEventDelegateContainer<T>` — stores `Func<Task>` directly.

### EventAccessor\<TEnum\> / EventAccessor\<TEnum, TArgs\>

Lightweight `readonly struct` values returned by generated event properties. They overload `operator +` and `operator -` to translate `+=` / `-=` syntax into `AddNewDelegate` / `RemoveDelegate` calls. The no-op setter on the generated property allows the compound assignment to compile.

---

## Concurrency Model

### Write Path (Subscribe / Unsubscribe)

All mutations to handler lists are performed under `Event<T>._lock`. This includes:
- Adding/removing containers from priority buckets
- Rebuilding the flat snapshot array
- Rebuilding per-`TArgs` typed snapshot arrays

The lock scope is intentionally narrow — only the mutation and rebuild are locked. Snapshot references are assigned atomically (reference assignment is atomic in .NET).

### Read Path (Invoke)

`Invoke<TArgs>` reads the typed snapshot under the lock (a single reference read), then iterates the array **outside the lock**. Since the snapshot is an immutable array that is never modified after creation, this is safe even if concurrent mutations are rebuilding a new snapshot.

```csharp
// Simplified invoke hot path
EventDelegateContainer<T, TArgs>[] typedSnapshot;
lock (_lock)
{
    typedSnapshot = _typedSnapshots.TryGetValue(typeof(TArgs), out object? obj)
        ? (EventDelegateContainer<T, TArgs>[])obj
        : [];
}
// No lock held during iteration
for (int j = 0; j < typedSnapshot.Length; j++)
    typedSnapshot[j].Invoke(args);
```

### Global Instance Tracking

The static `s_globalSnapshot` array follows the same COW pattern. `GlobalInvokeEvent` reads the snapshot without locking and iterates each enabled manager.

---

## Copy-on-Write Strategy

The COW approach is central to Vortex's performance:

1. **Subscribe/Unsubscribe** — Modifies mutable bucket lists under lock, then allocates a new snapshot array. The old snapshot remains valid for any in-progress invocations.
2. **Invoke** — Reads the snapshot reference (atomic), iterates without locking. Zero allocations on the hot path.
3. **Batch mode** — `BeginBatch()` / `EndBatch()` defers snapshot rebuilds. Multiple mutations accumulate; a single rebuild occurs when the outermost batch ends.

This makes invocation O(n) with no synchronization overhead, at the cost of an O(n) rebuild on each subscription change (amortized to O(1) when batching).

---

## Type-Safety Enforcement

### Compile-Time (Source Generator)

The generator produces strongly-typed `Invoke` and `Subscribe` methods. For an event declared with `[EventArgs(typeof(ScoreArgs))]`, only `Action<ScoreArgs>` handlers can be passed to the generated `SubscribeOnScoreChanged` method.

### Runtime (EventArgsContract\<T\>)

`EventArgsContract<T>` builds a `Dictionary<T, Type?>` from reflection over the enum's `[EventArgs]` attributes at first access (once per closed generic `T`). Both `AddNewDelegate` and `InvokeEvent` validate against this contract:
- **Subscribe** with wrong type → `InvalidOperationException` (fail-fast)
- **Invoke** with wrong type → error logged via `EventSystemDiagnostics.LogError`, invocation skipped (graceful degradation)

---

## Typed Snapshot Optimization

Instead of iterating all handlers and performing per-element type checks, `Event<T>` maintains per-`TArgs` typed snapshot arrays:

```
_typedSnapshots[typeof(ScoreArgs)] → EventDelegateContainer<T, ScoreArgs>[]
_typedSnapshots[typeof(Unit)]      → EventDelegateContainer<T, Unit>[]
```

`Invoke<TArgs>` retrieves the matching array with a single dictionary lookup and one reference cast. The array builder delegates are cached in a static `ConcurrentDictionary` so that the reflection cost of `MakeGenericMethod` is paid only once per `TArgs` type.

---

## Cancellation

The `CancellableCheck<TArgs>` static generic class evaluates `typeof(ICancellable).IsAssignableFrom(typeof(TArgs))` once at JIT time. When `TArgs` does not implement `ICancellable`, the cancellation check in `Invoke` is compiled away by the JIT as a dead branch (the static readonly bool is a JIT constant).

When cancellation is active:
```csharp
if (CancellableCheck<TArgs>.IsCancellable && args is ICancellable { Cancelled: true })
    break;
```

This pattern avoids boxing value-type args for the interface check in the non-cancellable case.

---

## Disposal & Lifecycle

- **EventDelegateContainer** — `Dispose()` calls `Event.Remove(this)`, enabling `using` patterns.
- **EventManager** — `Dispose()` disables all events, clears the dictionary, and removes itself from the static instance list and global snapshot. A finalizer logs a warning if `Dispose()` was not called.
- **Instance domains** — The generated `Manager` property exposes the `EventManager<T>`. The owning object should call `Manager.Dispose()` when it is no longer needed.
- **Static domains** — The manager lives for the process lifetime. Cleanup is typically unnecessary.
