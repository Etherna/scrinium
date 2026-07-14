# MongODM

MongODM is an **ODM framework** (Object-Documental Mapper) for **MongoDB** on .NET, oriented to Asp.NET Core applications. It maps domain objects to documents, manages denormalized references between documents (with automatic dependency updates), versioned document schemas on the same collection, and data migrations. It is a set of **libraries**, not an application.

## Build, run, test

Libraries multi-target **.NET 8, 9 and 10**; the test projects target **.NET 10 only**. `TreatWarningsAsErrors=true` and `AnalysisMode=AllEnabledByDefault` are set everywhere — warnings break the build, on every target framework.

```bash
dotnet restore MongODM.sln
dotnet build MongODM.sln -c Release                  # compiles every target framework
dotnet test  MongODM.sln -c Release                  # runs the xUnit test projects
dotnet test test/MongODM.Core.UnitTests/MongODM.Core.UnitTests.csproj    # single project
dotnet test --filter "FullyQualifiedName~DbContextTest"          # single class
dotnet test --filter "FullyQualifiedName~DbContextTest.CanRunExclusiveAccess"  # single test
dotnet run  --project samples/AspNetCoreSample       # sample web app (requires a local MongoDB)
```

Because the libraries compile against the lowest target (`net8.0`), do not use APIs introduced only in a later framework — it will pass locally on `net10.0` and fail the build on `net8.0`. A green `MongODM.sln` build means all frameworks compiled.

Integration tests (`test/MongODM.IntegrationTests`) need a real MongoDB instance: they use the `MONGODM_TEST_DB_URL` environment variable when set (CI provides a `mongo` service container), otherwise they spawn a throwaway local `mongod` process (the binary must be on `PATH`).

Versioning is computed by **GitVersion** (no manual version bumps). CI (`.github/workflows/`) publishes unstable packages to MyGet from `dev`, and stable packages to NuGet from version tags.

## Architecture

Five source projects (one NuGet package each), two test projects, one sample:

- **`src/MongODM.Core`** (`Etherna.MongODM.Core`) — The framework itself, host-agnostic. Main areas:
  - `DbContext.cs` / `IDbContext.cs` / `IDbContextEngine.cs` — unit of work: owns repositories, serialization registries, seeding, migrations, and exclusive access locking (`RunWithExclusiveAccessAsync`). `IDbContextEngine` exposes the scope independent engine members (connections, registries, infrastructure), and is the type captured by the serialization pipeline; `IDbContext` extends it with the current unit of work state (`ChangedModelsList`, `SaveChangesAsync`).
  - `Repositories/` — `Repository<TModel, TKey>` with typed access to collections; every collection access passes through `AccessToCollectionAsync`.
  - `Serialization/` — model maps and versioned schemas (`MapRegistry`, `ModelMap`, member maps), discriminator registry, serializers and modifiers.
  - `ProxyModels/` — Castle DynamicProxy-based model proxies enabling lazy loading and change auditing (`IAuditable`).
  - `Migration/` — `DocumentMigration` scripts between document schemas.
  - `Tasks/` — background tasks invoked through `ITaskRunner` (`UpdateDocDependenciesTask` propagates updated summaries to referencing documents; `MigrateDbContextTask` runs a db context migration under exclusive access).
  - `Utility/` — `LoadedModelsTracker` (tracks models loaded in the current execution scope, source of `ChangedModelsList` for `SaveChangesAsync`), `DbMaintainer` (enqueues dependency updates on model changes), `DbMigrationManager`, `ExclusiveAccessHandler` + `LimitedAccessMongoCollection` (deny read/write access to collections while another context holds exclusive access).
  - `Domain/Models/` — internal operation log entities persisted in the `_db_ops` collection (`SeedOperation`, `DbMigrationOperation` with its `DbMigrationOpAgg/` logs).
- **`src/MongODM.AspNetCore`** (`Etherna.MongODM.AspNetCore`) — DI integration: `AddMongODM` configuration builder, singleton `DbContext` registration, `DbDependencies`, execution context wiring.
- **`src/MongODM.AspNetCore.UI`** (`Etherna.MongODM.AspNetCore.UI`) — Admin dashboard as a Razor Pages area (`Areas/MongODM/Pages/Index*`), mapped on a configurable `DashboardOptions.BasePath` and guarded by `IDashboardAuthFilter`s. Static assets are self-contained in `wwwroot/` (no external client libraries); the status/start endpoints are page handlers polled by `wwwroot/js/mongodmDash.js`.
- **`src/MongODM.Hangfire`** (`Etherna.MongODM.Hangfire`, root namespace `Etherna.MongODM.HF`) — `ITaskRunner` implementation scheduling MongODM tasks on Hangfire.
- **`src/MongODM`** (`Etherna.MongODM`) — Meta package wiring the full stack (AspNetCore + Hangfire) with a single `AddMongODMWithHangfire` entry point.
- **`test/MongODM.Core.UnitTests`** — xUnit + Moq unit tests for the core.
- **`test/MongODM.IntegrationTests`** — xUnit integration tests against a real MongoDB instance, pinning end-to-end behavior (change tracking, referenced models and lazy loading, execution scope isolation).
- **`samples/AspNetCoreSample`** — runnable demo app (not packed).

Key cross-cutting points:

- **The MongoDB driver is the Etherna fork** (`Etherna.MongoDB.Driver`, namespaces `Etherna.MongoDB.*`) — never reference the official `MongoDB.*` packages or namespaces.
- **Execution contexts**: `IExecutionContext` (`Etherna.MongODM.Core.ExecContext` namespaces, vendored from the former standalone `ExecutionContext` package) provides ambient per-flow state (HTTP request or async-local scope). `DbExecutionContextHandler` associates a db context to the flow; `ExclusiveAccessHandler` marks a flow as owner of an exclusive access. Handlers are `IDisposable` scopes registered in `context.Items`.
- **DbContexts are singletons**: state on a `DbContext` instance (exclusive access flags, seeding state) is shared by all requests of the process.
- **Exclusive access locking**: `RunWithExclusiveAccessAsync` (used by seeding and migrations) sets `IsExclusiveReadEnabled`/`IsExclusiveWriteEnabled`; while set, `LimitedAccessMongoCollection` throws `UnauthorizedAccessException` for any flow not holding an `ExclusiveAccessHandler`. Anything that must keep working during a migration (e.g. migration status reads for the dashboard) has to create its own handler scope.
- **`ConfigureAwait(false)` is required** on every awaited call in library code — these are libraries with no synchronization context to preserve.
- **Logging** uses strongly-typed `LoggerMessage.Define` delegates in each project's `Extensions/LoggerExtensions.cs`, grouped by level, with incremental never-reused event ids; the header comment `Last event id is: N` is the source of truth for the next free id — update it when adding a delegate.
- Every source file starts with the standard **LGPL** copyright header (see any existing file); the solution license is GNU LGPL (`COPYING-LESSER`).

## Issue tracker

Bugs and features are tracked in Jira project **MODM** (https://etherna.atlassian.net/projects/MODM). Branch names follow `feature/MODM-<id>-<slug>` / `improve/MODM-<id>-<slug>` / `fix/MODM-<id>-<slug>` — match this when creating branches.

# Coding Style

## General Principles

- Keep commits clean: only include changes strictly necessary for the task at hand.
- Keep `README.md` aligned: when a change touches configuration, features, build/run steps, or architecture, update `README.md` in the same change.
- Never reference AI agents or assistants in commits or code — no agent names, no `Co-Authored-By` agent trailers, no "generated/assisted by" notes. Commit messages and code must read as the team's own work.
- Exceptions to these conventions are accepted when strictly necessary or when they significantly improve code quality. Justify with a comment where needed.
- All elements (usings, properties, methods, fields, enum members, etc.) are always alphabetically ordered within their respective sections.
- Primary constructors are preferred everywhere the constructor is a simple parameter assignment.
- Keep code clean: remove unused variables, dead code, and redundant imports.

## Naming

- **Classes/Structs**: PascalCase (`DbMigrationManager`, `ModelMap`)
- **Interfaces**: `I` prefix (`IDbContext`, `ITaskRunner`)
- **Async methods**: always `Async` suffix (`SaveChangesAsync`, `FindOneAsync`)
- **Properties**: PascalCase (`IsSeeded`, `CurrentStatus`)
- **Private fields**: `_camelCase` only when backing a same-named property (`_isSeeded` for `IsSeeded`); otherwise plain `camelCase`
- **Primary constructor parameters**: `camelCase` without underscore
- **Constants**: PascalCase (`HandlerKey`)
- **Enums**: PascalCase type and members (`ExecutionState.Succeded`)
- **Namespaces**: root namespace of the project + folder path (e.g. `Etherna.MongODM.Core.Utility`); note `MongODM.Hangfire` uses the `Etherna.MongODM.HF` root
- **Custom exceptions**: `Mongodm` prefix and `Exception` suffix (`MongodmEntityNotFoundException`), under `Exceptions/`

## Code Organization

- One class per file, filename matches class name
- Namespace mirrors folder structure (under the project's root namespace)
- Block-scoped namespaces: `namespace X { ... }` — NOT file-scoped
- Using directives outside the namespace block, always alphabetically ordered and kept to the minimum necessary
- No global usings — each file declares its own imports

## Comments

Principal comments (generally multiline, important):
```csharp
// Capital start, ending period.
// Continued on next line if needed.
```

Secondary/separator comments:
```csharp
//no space, no capital, no ending period
```

Longer explanations of non-obvious behavior use `/* ... */` blocks. Comment only what helps a future reader: non-obvious behavior, intent, or a gotcha. Do **not** write narration of your own reasoning or decisions — that belongs in the commit message / PR description, never in committed code.

## Member Ordering Within a Class

Use principal-style section comments to delimit groups, in this order:

```csharp
// Consts.
public const int MaxLength = 100;

// Fields.
private List<Item> _items = [];

// Constructors.
public MyType(string name) { ... }

// Initializer.
public void Initialize(...) { ... }

// Properties.
public string Name { get; }

// Methods.
public void DoSomething() { ... }

// Helpers.
private void InternalHelper() { ... }
```

## Class Design

- `internal sealed` for implementations that aren't part of the public API
- Primary constructors everywhere the constructor is a simple assignment
- Framework-initialized components implement `IDbContextInitializable` (`Initialize(dbContext, logger)` + `IsInitialized` guard) instead of taking the db context in the constructor

### Persisted Model Classes

- `virtual` on all properties and methods for proxy support
- Protected parameterless constructor for deserialization
- Collection encapsulation with a private backing field exposed as `IEnumerable<T>`, never `null` (at most empty)
- `[PropertyAlterer(nameof(MyProp))]` on every method, for each property the method modifies — required by change tracking

## Async Patterns

- Always suffix with `Async`
- `CancellationToken cancellationToken = default` as the optional last parameter (non-nullable, `default` — not `CancellationToken? = null`)
- Return `Task` or `Task<T>`, never `async void`
- `ConfigureAwait(false)` on **every** awaited call in library code

## Null Handling

- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- `ArgumentNullException.ThrowIfNull(param, nameof(param))` for parameter validation
- `is null` / `is not null` (not `== null`)
- Prefer `null` over `default` as default value for optional reference parameters
- `= null!` on non-nullable members assigned after construction (e.g. by `Initialize`)
- `??` and `??=` operators

## Formatting

- Allman braces (opening brace on new line)
- 4-space indentation
- Expression-bodied members for single expressions
- LINQ method chains: one operation per line, aligned
- Blank line between member sections

## C# Language Features

- Pattern matching: `is`, `is not`, type patterns, property patterns
- Switch expressions for multi-branch returns
- Primary constructors everywhere applicable
- Collection expressions: `[]`, `[..spread]`
- Prefer collection expressions over constructors to initialize any collection: `[]` not `new()`, `["a", "b"]` not `new List<string> { "a", "b" }`. Use a constructor only when a collection expression can't express the intent (e.g. presizing capacity with `new List<T>(capacity)`).
- Target-typed `new()` when type is clear from context (for non-collection types)

## LINQ

- Method syntax preferred over query syntax
- Query syntax only for complex join/groupby with multiple `from` clauses
- Fluent chaining, one operation per line for readability

## Testing (xUnit + Moq)

- `[Fact]` for basic tests, `[Theory]` with `[InlineData]`/`[MemberData]` for parameterized cases
- xUnit assertions: `Assert.Equal()`, `Assert.NotNull()`, `Assert.ThrowsAsync<T>()`
- Moq for mocking: `new Mock<IDbContext>()`
- **No `ConfigureAwait` in test code** — inside tests write plain `await foo()`; the library rule applies to `src/` only
- The test project mirrors the `MongODM.Core` layout
