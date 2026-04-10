# Source Generator

Vortex includes an incremental Roslyn source generator (`Vortex.Generators`) that processes `[EventDomain]` classes and emits all the boilerplate code at compile time. This document explains how it works, what it generates, and how to troubleshoot.

---

## Overview

The generator lives in the `Vortex.Generators` project (targeting .NET Standard 2.0 for broad compiler compatibility) and is referenced by the main `Vortex` project as an analyzer:

```xml
<ProjectReference Include="..\Vortex.Generators\Vortex.Generators.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

It uses the **incremental generator** API (`IIncrementalGenerator`) for efficient IDE integration — only re-running when the relevant syntax or attributes change.

---

## How It Works

### 1. Discovery

The generator uses `ForAttributeWithMetadataName` to find all `ClassDeclarationSyntax` nodes decorated with `Vortex.EventDomainAttribute`. This is the most efficient discovery mechanism, as Roslyn filters by attribute name before invoking the transform.

### 2. Extraction

For each discovered class, `GetDomainInfo` extracts:

| Field | Source |
|---|---|
| `Namespace` | `INamedTypeSymbol.ContainingNamespace` |
| `ClassName` | `INamedTypeSymbol.Name` |
| `IsStatic` | `INamedTypeSymbol.IsStatic` |
| `IsGlobal` | `[EventDomain(Global = true)]` named argument |
| `IsPartial` | Checks for `partial` keyword on the syntax node |
| `ContainingTypes` | Walks `ContainingType` chain for nested classes |
| `Events` | All `IFieldSymbol` members with type `Vortex.EventKey` |

For each `EventKey` field:
- The field name is used as the event name (with leading `_` stripped).
- If `[EventArgs(typeof(T))]` is present, `T` is the args type; otherwise `Unit` is used.

### 3. Caching

All extracted data is stored in value-equatable structs (`EventDomainInfo`, `EventKeyInfo`, `ContainingTypeInfo`) and an `EquatableArray<T>` wrapper. This ensures the incremental pipeline correctly caches results and only re-generates when actual changes occur.

### 4. Code Generation

`Execute` builds the output source string using `StringBuilder`. For each domain class, it generates:

---

## Generated Output

Given this input:

```csharp
namespace MyGame;

[EventDomain(Global = true)]
public static partial class GameEvents
{
    private static readonly EventKey _OnStarted = new();

    [EventArgs(typeof(ScoreArgs))]
    private static readonly EventKey _OnScoreChanged = new();

    public readonly record struct ScoreArgs(int Old, int New);
}
```

The generator produces `GameEvents.g.cs`:

### Enum

```csharp
public enum EventTypes
{
    [global::Vortex.EventArgs(typeof(global::Vortex.Unit))]
    OnStarted,

    [global::Vortex.EventArgs(typeof(global::MyGame.GameEvents.ScoreArgs))]
    OnScoreChanged,
}
```

Each enum value carries the `[EventArgs]` attribute so that `EventArgsContract<T>` can discover the type mapping at runtime via reflection.

### Manager

```csharp
private static readonly global::Vortex.EventManager<EventTypes> s_eventManager = new(global: true);

public static global::Vortex.EventManager<EventTypes> Manager => s_eventManager;
```

For instance (non-static) domains, the field is `_eventManager` and non-static.

### Event Accessor Properties

```csharp
// Parameterless event
public static global::Vortex.EventAccessor<EventTypes> OnStarted
{
    get => new(s_eventManager, EventTypes.OnStarted);
    set { }  // no-op setter enables += / -= syntax
}

// Typed event
public static global::Vortex.EventAccessor<EventTypes, global::MyGame.GameEvents.ScoreArgs> OnScoreChanged
{
    get => new(s_eventManager, EventTypes.OnScoreChanged);
    set { }
}
```

The no-op setter is necessary because `+=` on a property expands to `prop = prop + value`, which requires a setter.

### Per-Event Convenience Methods

For each event, the generator emits:

#### Parameterless Events (`Unit`)

| Method | Description |
|---|---|
| `InvokeOnStarted()` | Invoke on this domain's manager |
| `InvokeOnStartedAsync()` | Async invoke |
| `GlobalInvokeOnStarted()` | Broadcast across all global managers |
| `GlobalInvokeOnStartedAsync()` | Async global broadcast |
| `SubscribeOnStarted(Action, int)` | Subscribe with priority, returns disposable |
| `SubscribeOnStartedAsync(Func<Task>, int)` | Subscribe async handler |

#### Typed Events

| Method | Description |
|---|---|
| `InvokeOnScoreChanged(ScoreArgs)` | Invoke with args |
| `InvokeOnScoreChangedAsync(ScoreArgs)` | Async invoke |
| `GlobalInvokeOnScoreChanged(ScoreArgs)` | Global broadcast |
| `GlobalInvokeOnScoreChangedAsync(ScoreArgs)` | Async global broadcast |
| `SubscribeOnScoreChanged(Action<ScoreArgs>, int)` | Subscribe with priority |
| `SubscribeOnScoreChangedAsync(Func<ScoreArgs, Task>, int)` | Subscribe async |

### DEBUG Conditional Code

Subscribe methods include `#if DEBUG` / `#else` blocks. In debug builds, caller info parameters (`CallerFilePath`, `CallerLineNumber`, `CallerMemberName`) are captured automatically and forwarded to the container.

---

## Static vs. Instance Domains

| Aspect | Static Domain | Instance Domain |
|---|---|---|
| Class modifier | `public static partial class` | `public partial class` |
| Manager field | `private static readonly ... s_eventManager` | `private readonly ... _eventManager` |
| Generated methods | `public static` | `public` (instance) |
| `GlobalInvoke` methods | Always `static` | Always `static` |
| Manager property | `public static Manager` | `public Manager` |
| Lifetime | Process-scoped | Owner-scoped (call `Manager.Dispose()`) |

---

## Nested Classes

The generator supports event domains nested inside other classes. It reconstructs the full nesting chain and emits matching `partial` wrappers:

```csharp
public partial class Outer
{
    [EventDomain]
    public partial class InnerEvents
    {
        private static readonly EventKey _OnSomething = new();
    }
}
```

Generates:
```csharp
public partial class Outer
{
    public partial class InnerEvents
    {
        public enum EventTypes { ... }
        // ...
    }
}
```

---

## Diagnostics

### PEVT0001 — EventDomain class must be partial

**Severity:** Error

Emitted when a class has `[EventDomain]` but is not declared `partial`. The generator cannot extend a non-partial class.

**Fix:** Add the `partial` modifier:
```csharp
// Before
[EventDomain]
public static class MyEvents { ... }

// After
[EventDomain]
public static partial class MyEvents { ... }
```

---

## Troubleshooting

### Generated code not appearing

1. Ensure the `Vortex.Generators` project is referenced as an analyzer:
   ```xml
   <ProjectReference Include="...\Vortex.Generators.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```
2. Clean and rebuild the solution.
3. Check the `obj/Debug/.../generated/` folder for generated `.g.cs` files.

### IntelliSense not recognizing generated members

Restart Visual Studio or use **Build → Rebuild Solution**. The incremental generator requires a build pass for IntelliSense to discover new output.

### "Class must be partial" error

Add the `partial` keyword to your domain class declaration. See diagnostic **PEVT0001** above.

### Events not generating

`EventKey` fields with no leading underscore still work — the name is used as-is. However, ensure:
- The field type is exactly `Vortex.EventKey`.
- The field is declared inside a class marked with `[EventDomain]`.
- There is at least one `EventKey` field (empty domains are skipped).
