# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

---

## [Unreleased]

### Added
- Core event system: `EventManager<T>`, `Event<T>`, priority-sorted handler chains.
- Copy-on-write snapshot strategy for lock-free invocation.
- Per-`TArgs` typed snapshot arrays for zero-cast invocation.
- `[EventDomain]` source generator producing enums, managers, and convenience methods.
- `[EventArgs]` attribute for compile-time and runtime type-safety enforcement.
- `EventAccessor<T>` / `EventAccessor<T, TArgs>` structs supporting `+=` / `-=` syntax.
- Async handler support via `AsyncEventDelegateContainer<T, TArgs>` and `InvokeAsync`.
- `ICancellable` interface for handler chain propagation control.
- `IEventManagerHolder<T>` interface and `InvokeEvents` extension methods for collection-based invocation.
- Batched mutations via `BeginBatch()` / `EndBatch()` for deferred snapshot rebuilds.
- Global broadcast via `GlobalInvokeEvent` across all global manager instances.
- `EventSystemDiagnostics` configurable logging hooks.
- DEBUG-only diagnostics: slow handler detection, caller info capture, type-mismatch warnings, sync-over-async warnings.
- Parameterless handler specializations (`ParameterlessEventDelegateContainer`, `ParameterlessAsyncEventDelegateContainer`) avoiding closure allocations.
- AOT compatibility (`IsAotCompatible`).
- Comprehensive documentation: README, Architecture, API Reference, Source Generator, Advanced Usage, Contributing.
