# Contributing to Vortex

Thank you for your interest in contributing to Vortex! This document outlines the guidelines and process for contributing.

---

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Visual Studio 2022 17.8+ or VS Code with the C# Dev Kit
- Basic familiarity with Roslyn source generators (for generator work)

### Building

```bash
git clone https://github.com/<org>/Vortex.git
cd Vortex
dotnet build
```

### Project Structure

```
Vortex/
├── Vortex/                    # Core event system library (.NET 9)
│   ├── EventManager.cs        # Central event bus
│   ├── Event.cs               # Per-event slot with COW snapshots
│   ├── EventDelegateContainer.cs  # Handler containers
│   ├── AsyncEventDelegateContainer.cs
│   ├── EventAccessor.cs       # += / -= support structs
│   ├── EventDomainAttribute.cs
│   ├── EventArgsAttribute.cs
│   ├── EventArgsContract.cs   # Runtime type validation
│   ├── EventKey.cs            # Marker type for event declarations
│   ├── EventParam.cs          # Unit struct
│   ├── EventSystemDiagnostics.cs
│   ├── ICancellable.cs
│   ├── IInvocable.cs
│   ├── IAsyncInvocable.cs
│   └── IEventManagerHolder.cs
├── Vortex.Generators/         # Roslyn source generator (.NET Standard 2.0)
│   ├── EventDomainGenerator.cs
│   └── EquatableArray.cs
├── docs/                      # Documentation
└── README.md
```

---

## Development Guidelines

### Code Style

- Follow the existing code style in the project.
- Use `readonly struct` for small value types.
- Prefer explicit types over `var` for clarity in public APIs.
- Implicit usings are disabled — add explicit `using` statements.
- Nullable reference types are enabled — annotate accordingly.
- Avoid adding comments that restate the code; prefer XML doc comments on public APIs.

### Concurrency

- All mutations to handler lists must occur under `Event<T>._lock`.
- Snapshot references are assigned atomically — readers must never modify snapshot arrays.
- Use copy-on-write patterns for any new shared data structures.

### Performance

- The invoke path is the hot path. Avoid allocations, virtual calls, or locking during invocation.
- Benchmark any changes to `Event<T>.Invoke` or snapshot rebuild logic.
- The source generator targets .NET Standard 2.0 — avoid APIs not available on that target.

### AOT Compatibility

The library is marked `IsAotCompatible`. Avoid:
- Unrestricted `MakeGenericMethod` / `MakeGenericType` (existing uses are cached and bounded).
- `Type.GetType(string)` or assembly loading patterns.
- Dynamic code emission (`Reflection.Emit`).

---

## Making Changes

### 1. Fork and Branch

```bash
git checkout -b feature/my-change
```

### 2. Make Your Changes

- Keep changes focused and minimal.
- Update documentation if you add or change public APIs.
- Add XML doc comments to new public types and members.

### 3. Build and Test

```bash
dotnet build
dotnet test  # if tests are available
```

Ensure:
- The build succeeds with zero warnings (the project suppresses specific warnings via `<NoWarn>` — do not add to this list without discussion).
- No new compiler errors or analyzer violations.

### 4. Submit a Pull Request

- Write a clear description of what changed and why.
- Reference any related issues.
- Keep commits logically organized.

---

## Source Generator Development

The `Vortex.Generators` project is a Roslyn incremental source generator. Key considerations:

### Testing Generator Changes

1. Make your changes in `EventDomainGenerator.cs`.
2. Rebuild the solution.
3. Check the generated output in `obj/Debug/net9.0/generated/Vortex.Generators/` of the consuming project.
4. Verify IntelliSense picks up the changes (may require a VS restart).

### Incremental Correctness

- All data passed through the pipeline must be value-equatable. Use `EquatableArray<T>` for collections.
- Test that modifying an `[EventDomain]` class triggers regeneration, and that unrelated file changes do **not**.

### .NET Standard 2.0 Constraints

The generator must compile against .NET Standard 2.0. This means:
- No `Span<T>`, `ReadOnlySpan<T>`, or `System.Range`.
- No default interface methods.
- No `init`-only setters (use `LangVersion: latest` features that compile to Standard 2.0).
- Use `Microsoft.CodeAnalysis.CSharp` 4.8.0 APIs.

---

## Reporting Issues

When filing an issue, please include:
- .NET SDK version (`dotnet --version`)
- Visual Studio version (if applicable)
- Minimal reproduction code
- Full error message or diagnostic output
- Expected vs. actual behavior

---

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
