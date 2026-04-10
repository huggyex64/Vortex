# API Reference

Complete public API documentation for the Vortex event system.

---

## Table of Contents

- [Attributes](#attributes)
  - [EventDomainAttribute](#eventdomainattribute)
  - [EventArgsAttribute](#eventargsattribute)
- [Core Types](#core-types)
  - [EventManager\<T\>](#eventmanagert)
  - [Event\<T\>](#eventt)
  - [EventKey](#eventkey)
  - [Unit](#unit)
- [Delegate Containers](#delegate-containers)
  - [EventDelegateContainer\<T\>](#eventdelegatecontainert)
  - [EventDelegateContainer\<T, TArgs\>](#eventdelegatecontainert-targs)
  - [AsyncEventDelegateContainer\<T, TArgs\>](#asynceventdelegatecontainert-targs)
  - [ParameterlessEventDelegateContainer\<T\>](#parameterlesseventdelegatecontainert)
  - [ParameterlessAsyncEventDelegateContainer\<T\>](#parameterlessasynceventdelegatecontainert)
- [Accessors](#accessors)
  - [EventAccessor\<TEnum\>](#eventaccessortenum)
  - [EventAccessor\<TEnum, TArgs\>](#eventaccessortenum-targs)
- [Interfaces](#interfaces)
  - [ICancellable](#icancellable)
  - [IInvocable\<TArgs\>](#iinvocabletargs)
  - [IAsyncInvocable\<TArgs\>](#iasyncinvocabletargs)
  - [IEventManagerHolder\<T\>](#ieventmanagerholdert)
- [Diagnostics](#diagnostics)
  - [EventSystemDiagnostics](#eventsystemdiagnostics)

---

## Attributes

### EventDomainAttribute

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventDomainAttribute : Attribute
```

Marks a `partial class` as an event domain. The source generator processes this attribute to produce the backing enum, `EventManager<T>`, and all convenience methods.

| Property | Type | Default | Description |
|---|---|---|---|
| `Global` | `bool` | `false` | When `true`, the generated manager is created with `global: true`, making it participate in `GlobalInvoke` calls. |

**Usage:**
```csharp
[EventDomain(Global = true)]
public static partial class MyEvents { ... }
```

---

### EventArgsAttribute

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EventArgsAttribute : Attribute
```

Declares the canonical `TArgs` type for an `EventKey` field. When present, both subscribe and invoke operations validate the type at runtime.

| Parameter | Type | Description |
|---|---|---|
| `argsType` | `Type` | The argument type for the event. |

**Usage:**
```csharp
[EventArgs(typeof(MyPayload))]
private static readonly EventKey _OnSomething = new();
```

When omitted, the event defaults to `Unit` (parameterless).

---

## Core Types

### EventManager\<T\>

```csharp
public class EventManager<T> : IDisposable where T : struct, Enum
```

The central event bus, keyed by an enum type `T`. Each enum value maps to one `Event<T>` instance.

#### Constructor

| Parameter | Type | Default | Description |
|---|---|---|---|
| `global` | `bool` | `false` | Whether this manager participates in global broadcast. |

#### Properties

| Property | Type | Description |
|---|---|---|
| `Global` | `bool` | Gets or sets whether this manager is included in global snapshots. |
| `Enabled` | `bool` | Gets or sets the enabled state. When `false`, all invocations are no-ops. |

#### Static Properties

| Property | Type | Description |
|---|---|---|
| `LastGlobalInstance` | `EventManager<T>?` | Returns the last enabled global manager instance, or `null`. |

#### Methods — Subscription

| Method | Returns | Description |
|---|---|---|
| `AddNewDelegate<TArgs>(T, Action<TArgs>, int)` | `EventDelegateContainer<T, TArgs>` | Register a typed handler with optional priority. |
| `AddNewDelegate(T, Action, int)` | `EventDelegateContainer<T, Unit>` | Register a parameterless handler. |
| `AddNewAsyncDelegate<TArgs>(T, Func<TArgs, Task>, int)` | `AsyncEventDelegateContainer<T, TArgs>` | Register a typed async handler. |
| `AddNewAsyncDelegate(T, Func<Task>, int)` | `AsyncEventDelegateContainer<T, Unit>` | Register a parameterless async handler. |
| `AddDelegate(EventDelegateContainer<T>)` | `void` | Add a pre-built container. |
| `RemoveDelegate(EventDelegateContainer<T>)` | `void` | Remove a specific container. |
| `RemoveDelegate(T, Delegate)` | `bool` | Remove the first container wrapping the given handler delegate. |

#### Methods — Invocation

| Method | Returns | Description |
|---|---|---|
| `InvokeEvent<TArgs>(T, TArgs)` | `void` | Invoke with typed arguments. |
| `InvokeEvent(T)` | `void` | Invoke parameterless (shorthand for `InvokeEvent(T, default(Unit))`). |
| `InvokeEventAsync<TArgs>(T, TArgs)` | `Task` | Async invoke — awaits async handlers sequentially. |
| `InvokeEventAsync(T)` | `Task` | Async invoke parameterless. |

#### Static Methods — Global Broadcast

| Method | Returns | Description |
|---|---|---|
| `GlobalInvokeEvent<TArgs>(T, TArgs)` | `void` | Invoke across all enabled global managers. |
| `GlobalInvokeEvent(T)` | `void` | Parameterless global invoke. |
| `GlobalInvokeEventAsync<TArgs>(T, TArgs)` | `Task` | Async global invoke. |
| `GlobalInvokeEventAsync(T)` | `Task` | Async parameterless global invoke. |

#### Methods — Event Management

| Method | Returns | Description |
|---|---|---|
| `GetEvent(T)` | `Event<T>?` | Returns the event instance if it exists, or `null`. |
| `RemoveEvent(Event<T>)` | `void` | Remove an event from the manager. |
| `EnableEvent(T)` | `void` | Enable a specific event type. |
| `DisableEvent(T)` | `void` | Disable a specific event type. |

#### Methods — Batching

| Method | Returns | Description |
|---|---|---|
| `BeginBatch()` | `void` | Begin a batch — snapshot rebuilds are deferred. Calls may be nested. |
| `EndBatch()` | `void` | End a batch — rebuilds snapshots if mutations occurred. |

#### Disposal

`Dispose()` disables all events, clears the dictionary, and removes the manager from static tracking. The finalizer logs a warning if `Dispose()` was not called.

---

### Event\<T\>

```csharp
public class Event<T> where T : struct, Enum
```

Represents a single event slot for one enum value. Manages priority-sorted handler buckets and COW snapshots.

#### Properties

| Property | Type | Description |
|---|---|---|
| `EventType` | `T` | The enum value this event represents. |
| `EventManager` | `EventManager<T>` | The parent manager. |
| `Enabled` | `bool` | Whether this event is enabled. |

#### Static Properties (DEBUG only)

| Property | Type | Default | Description |
|---|---|---|---|
| `SlowHandlerThresholdMs` | `double` | `200.0` | Handlers exceeding this duration trigger a warning. Set to `0` to disable. |

#### Methods

| Method | Returns | Description |
|---|---|---|
| `Invoke<TArgs>(TArgs)` | `void` | Invoke all handlers matching `TArgs`, in priority order. |
| `InvokeAsync<TArgs>(TArgs)` | `Task` | Async invoke — awaits `IAsyncInvocable<TArgs>` handlers. |
| `Add(EventDelegateContainer<T>)` | `void` | Add a handler to this event. |
| `Remove(EventDelegateContainer<T>)` | `bool` | Remove a handler. |
| `RemoveByDelegate(Delegate)` | `bool` | Remove the first handler wrapping the given delegate. |
| `GetHandlers<TArgs>()` | `ReadOnlySpan<EventDelegateContainer<T, TArgs>>` | Snapshot of current handlers for the given `TArgs`. |
| `BeginBatch()` | `void` | Start batching. |
| `EndBatch()` | `void` | End batching, rebuild if dirty. |

---

### EventKey

```csharp
public readonly struct EventKey
```

A zero-size marker type. Declare `private static readonly EventKey` fields inside `[EventDomain]` classes to define events. The leading underscore is stripped by the generator to produce the public event name.

---

### Unit

```csharp
public readonly struct Unit
```

A zero-size struct used as `TArgs` for parameterless events. Avoids boxing and nullable overhead compared to using `object` or `EventArgs.Empty`.

---

## Delegate Containers

### EventDelegateContainer\<T\>

```csharp
public abstract class EventDelegateContainer<T> : IDisposable where T : struct, Enum
```

Non-generic base class for all handler containers. Provides shared infrastructure.

| Property | Type | Description |
|---|---|---|
| `EventManager` | `EventManager<T>?` | The parent manager (via the linked event). |
| `Event` | `Event<T>` | The event this container is registered with. |
| `EventType` | `T` | The enum value. |
| `Enabled` | `bool` | Whether this handler is enabled. |
| `Priority` | `int` | The priority (lower runs first). |
| `ArgsType` | `Type` | The `TArgs` type this container handles. |

| Method | Returns | Description |
|---|---|---|
| `Enable()` | `void` | Enable this handler. |
| `Disable()` | `void` | Disable this handler. |
| `Dispose()` | `void` | Unsubscribe from the parent event. |
| `MatchesDelegate(Delegate)` | `bool` | Whether this container wraps the given delegate. |

**DEBUG-only properties:**

| Property | Type | Description |
|---|---|---|
| `SourceFile` | `string?` | File path where the handler was registered. |
| `SourceLine` | `int` | Line number. |
| `SourceMember` | `string?` | Member name. |
| `SourceDescription` | `string` | Formatted "File:Line (Member)" string. |

---

### EventDelegateContainer\<T, TArgs\>

```csharp
public class EventDelegateContainer<T, TArgs> : EventDelegateContainer<T>, IInvocable<TArgs>
    where T : struct, Enum
```

Wraps an `Action<TArgs>`. Primary container type for synchronous handlers.

| Method | Returns | Description |
|---|---|---|
| `Invoke(TArgs)` | `void` | Invoke the wrapped delegate (if enabled). |

---

### AsyncEventDelegateContainer\<T, TArgs\>

```csharp
public class AsyncEventDelegateContainer<T, TArgs> : EventDelegateContainer<T, TArgs>, IAsyncInvocable<TArgs>
    where T : struct, Enum
```

Wraps a `Func<TArgs, Task>`. Used for async handlers.

| Method | Returns | Description |
|---|---|---|
| `Invoke(TArgs)` | `void` | Fires the handler without awaiting (logs warning in DEBUG). |
| `InvokeAsync(TArgs)` | `Task` | Properly awaits the async handler. |

---

### ParameterlessEventDelegateContainer\<T\>

```csharp
public sealed class ParameterlessEventDelegateContainer<T> : EventDelegateContainer<T, Unit>
    where T : struct, Enum
```

Stores an `Action` directly, avoiding the closure allocation of wrapping in `Action<Unit>`.

---

### ParameterlessAsyncEventDelegateContainer\<T\>

```csharp
public sealed class ParameterlessAsyncEventDelegateContainer<T> : AsyncEventDelegateContainer<T, Unit>
    where T : struct, Enum
```

Stores a `Func<Task>` directly for parameterless async events.

---

## Accessors

### EventAccessor\<TEnum\>

```csharp
public readonly struct EventAccessor<TEnum> where TEnum : struct, Enum
```

Returned by generated properties for parameterless events. Supports `+=` / `-=` with `Action` and `Func<Task>` handlers.

| Method | Description |
|---|---|
| `Invoke()` | Fire the event. |
| `InvokeAsync()` | Fire with async awaiting. |
| `operator +(accessor, Action)` | Subscribe a sync handler. |
| `operator -(accessor, Action)` | Unsubscribe a sync handler. |
| `operator +(accessor, Func<Task>)` | Subscribe an async handler. |
| `operator -(accessor, Func<Task>)` | Unsubscribe an async handler. |

---

### EventAccessor\<TEnum, TArgs\>

```csharp
public readonly struct EventAccessor<TEnum, TArgs> where TEnum : struct, Enum
```

Returned by generated properties for typed events. Same operator support with `Action<TArgs>` and `Func<TArgs, Task>`.

| Method | Description |
|---|---|
| `Invoke(TArgs)` | Fire the event with arguments. |
| `InvokeAsync(TArgs)` | Fire with async awaiting. |
| `operator +(accessor, Action<TArgs>)` | Subscribe a sync handler. |
| `operator -(accessor, Action<TArgs>)` | Unsubscribe a sync handler. |
| `operator +(accessor, Func<TArgs, Task>)` | Subscribe an async handler. |
| `operator -(accessor, Func<TArgs, Task>)` | Unsubscribe an async handler. |

---

## Interfaces

### ICancellable

```csharp
public interface ICancellable
{
    bool Cancelled { get; set; }
}
```

Implement on event argument types to allow handlers to stop further propagation. When a handler sets `Cancelled = true`, subsequent handlers in the priority chain are skipped.

---

### IInvocable\<TArgs\>

```csharp
public interface IInvocable<in TArgs>
{
    void Invoke(TArgs args);
}
```

Interface for typed, zero-cast synchronous invocation.

---

### IAsyncInvocable\<TArgs\>

```csharp
public interface IAsyncInvocable<in TArgs>
{
    Task InvokeAsync(TArgs args);
}
```

Interface for typed async invocation. `Event<T>.InvokeAsync` checks for this interface to determine whether to `await` a handler.

---

### IEventManagerHolder\<T\>

```csharp
public interface IEventManagerHolder<T> where T : struct, Enum
{
    EventManager<T> EventManager { get; }
}
```

Implement on types that own an `EventManager<T>` to use the `InvokeEvents` extension methods for batch invocation across collections of holders.

#### Extension Methods

```csharp
public static void InvokeEvents<T, TArgs>(this IEventManagerHolder<T>[] holders, T eventType, TArgs args)
public static void InvokeEvents<T, TArgs>(this List<IEventManagerHolder<T>> holders, T eventType, TArgs args)
public static void InvokeEvents<T>(this IEventManagerHolder<T>[] holders, T eventType)
public static void InvokeEvents<T>(this List<IEventManagerHolder<T>> holders, T eventType)
```

---

## Diagnostics

### EventSystemDiagnostics

```csharp
public static class EventSystemDiagnostics
```

Configurable logging hooks for the event system. Assign these during application startup to route diagnostics through your logging infrastructure.

| Property | Type | Description |
|---|---|---|
| `LogWarning` | `Action<string>?` | Non-critical messages: slow handlers, sync-over-async, type mismatches (DEBUG). |
| `LogError` | `Action<string>?` | Error-level messages: type mismatch on invoke. |

**Setup example:**
```csharp
EventSystemDiagnostics.LogWarning = msg => Console.WriteLine($"[WARN] {msg}");
EventSystemDiagnostics.LogError = msg => Console.Error.WriteLine($"[ERROR] {msg}");
```
