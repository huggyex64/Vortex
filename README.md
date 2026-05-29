# Vortex

### A high-performance, source-generated event system for .NET.

> **Zero-allocation invocation • Compile-time code generation • Type-safe by design**

Vortex is a modern **publish/subscribe event infrastructure** for .NET that lets you define entire event domains with a single attribute. The Roslyn source generator does the heavy lifting, producing clean, strongly-typed, high-performance event APIs at compile time.

---

## Features

- Source-generated event domains with `[EventDomain]` attribute
- Zero-allocation hot path invocation
- Built-in priority ordering
- Full sync + async handler support
- Type-safe argument contracts with `[EventArgs]`
- Cancellable events (`ICancellable`)
- Global broadcast support
- `IDisposable` subscriptions
- Excellent AOT and trimming compatibility
- Rich debug diagnostics

---

## Quick Start

### 1. Define your events

```csharp
using Vortex;

[EventDomain(Global = true)]
public static partial class GameEvents
{
    private static readonly EventKey _OnPlayerJoined = new();

    [EventArgs(typeof(ScoreChangedArgs))]
    private static readonly EventKey _OnScoreChanged = new();

    public readonly record struct ScoreChangedArgs(int OldScore, int NewScore);
}
```

### 2. Subscribe

```csharp
// Using familiar += syntax
GameEvents.OnScoreChanged += args => 
    Console.WriteLine($"Score changed: {args.OldScore} → {args.NewScore}");

// Or with priority and disposable handle
using var sub = GameEvents.OnScoreChanged.Subscribe(
    args => HandleScore(args), 
    priority: -50);
```

### 3. Invoke

```csharp
GameEvents.InvokeOnScoreChanged(new ScoreChangedArgs(100, 150));

// Async invoke
await GameEvents.InvokeOnScoreChangedAsync(new ScoreChangedArgs(100, 150));
```

---

## Installation

**NuGet package coming soon.**

For now, clone the repository and reference the `Vortex` project directly:

```xml
<ItemGroup>
  <ProjectReference Include="..\Vortex\Vortex.csproj" />
</ItemGroup>
```

**Current Version**: 1.1.5 (Latest)

See the [CHANGELOG](CHANGELOG.md) for recent changes.

---

## Why Vortex?

| Feature                    | Native C# Events | Traditional Event Buses | **Vortex**          |
|---------------------------|------------------|--------------------------|---------------------|
| Type Safety               | Excellent        | Often runtime casts      | **Compile-time + runtime contracts** |
| Boilerplate               | Low              | High                     | **Near zero (generated)** |
| Performance (hot path)    | Good             | Variable                 | **Zero allocation** |
| Priority Ordering         | No               | Sometimes                | **Built-in**        |
| Async Support             | Manual           | Varies                   | **First-class**     |
| AOT / Trimming            | Good             | Often problematic        | **Excellent**       |

---

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [API Reference](docs/API-REFERENCE.md)
- [Source Generator](docs/SOURCE-GENERATOR.md)
- [Advanced Usage](docs/ADVANCED-USAGE.md)

## Project Structure

- `Vortex/` — Core runtime library (.NET 9+)
- `Vortex.Generators/` — Roslyn incremental source generator

---

## License

MIT License. See [LICENSE](LICENSE) for details.

---

<p align="center">
  <strong>Vortex</strong> — Declare the events. Let the compiler build the system.<br>
  High performance. Maximum type safety. Minimal boilerplate.
</p>
