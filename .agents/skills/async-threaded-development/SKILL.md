---
name: async-threaded-development
description: Plan and implement asynchronous, thread-safe changes in Borne.Core services and infrastructure. Use when adding async I/O, coordinating concurrent work, or reviewing thread safety.
---

# Async Threaded Development

Use this skill when a task introduces asynchronous operations, concurrency, or shared mutable state. Read the root `AGENTS.md` and applicable project instructions first.

## Workflow

1. Identify the operation that can wait (database, file, or network I/O) and follow its async API end to end. Avoid wrapping synchronous I/O in `Task.Run`.
2. Preserve cancellation where the existing API supports it; pass `CancellationToken` through service and infrastructure boundaries rather than swallowing cancellation.
3. Do not block async code with `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`. Prefer `async`/`await` and return `Task` or `Task<T>`.
4. Check whether state is shared across calls. Prefer immutable inputs and per-operation database connections/commands. Add synchronization only when shared mutable state is required, and keep lock scopes small.
5. Dispose asynchronous resources with `await using` when supported by the target framework and provider. Respect the .NET Framework 4.7.2 core target when designing shared contracts.
6. Keep provider-specific concurrency and transaction handling in `borne.infrastructure/`; keep business orchestration in `borne.business/` and provider-neutral contracts in `borne.core/`.
7. Describe failure and cancellation behavior explicitly. Never log connection strings, credentials, or sensitive query data.

## Validation

Build the affected projects when requested or needed to validate compatibility. If tests exist or are requested, cover success, failure, cancellation, and concurrent access as relevant. Do not claim thread safety without identifying the state and synchronization boundary that make it safe.
