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

Integration tests (`test/MongODM.IntegrationTests`) need a real MongoDB instance supporting transactions: they use the `MONGODM_TEST_DB_URL` environment variable when set (CI runs a `mongo` container initiated as single node replica set), otherwise they spawn a throwaway local `mongod` process as a single node replica set (the binary must be on `PATH`).

Versioning is computed by **GitVersion** (no manual version bumps). CI (`.github/workflows/`) publishes unstable packages to MyGet from `dev`, and stable packages to NuGet from version tags.

## Architecture

Five source projects (one NuGet package each), two test projects, one sample:

- **`src/MongODM.Core`** (`Etherna.MongODM.Core`) — The framework itself, host-agnostic. Main areas:
  - `DbContext.cs` / `IDbContext.cs` / `DbContextEngine.cs` / `IDbContextEngine.cs` — unit of work and its engine, related by composition (`IDbContext` does NOT extend `IDbContextEngine`). `DbContextEngine` owns the scope independent members built once at initialization (connections, schema registries, seeding cache, exclusive access locking with `RunWithExclusiveAccessAsync`), and is the type captured by the serialization pipeline; `DbContext` attaches to its engine (exposed as `IDbContext.Engine`) and owns the current unit of work state (`ChangedModelsList`, `SaveChangesAsync`, per-instance repositories, migration facades, `ExecuteInTransactionAsync`).
  - `Repositories/` — `Repository<TModel, TKey>` with typed access to collections; every collection access passes through `AccessToCollectionAsync`.
  - `Serialization/` — model maps and versioned schemas (`MapRegistry`, `ModelMap`, member maps), discriminator registry, serializers and modifiers. Model map schema ids must be unique across the whole db context, not only inside their model map, with the `fallback` id reserved to fallback schemas: violations fail fast at engine build with a detailed `MongodmDuplicateSchemaIdException`. Reference serializer configurations are separate id spaces (their summary schema ids can mirror the root ones) and stay out of the check. Reference documents with an unrecognized or missing schema id deserialize through the configured fallback serializer or schema when present, or by default reading only the reference id (any other member lazy loads from the origin document).
  - `ProxyModels/` — Castle DynamicProxy-based model proxies enabling lazy loading of referenced models (`IReferenceable`). Change tracking is snapshot based and lives on the db context, not on a proxy interface: a proxy only flags itself a change candidate on mutation (`ChangeTrackingInterceptor`), so non proxy models are trackable too.
  - `Migration/` — `DocumentMigration` scripts between document schemas.
  - `Tasks/` — background tasks invoked through `ITaskRunner` (`UpdateDocDependenciesTask` propagates updated summaries to referencing documents; `MigrateDbContextTask` runs a db context migration under exclusive access).
  - `Utility/` — `DbMaintainer` (enqueues dependency updates on model changes), `DbMigrationManager` (drives db context migrations and their `DbMigrationOperation` log: any failure, unhandled exceptions included, marks the operation failed — never left on running status, which would block every future migration — logs at error level, and throws only when the caller asks for errors), `ExclusiveAccessHandler` + `LimitedAccessMongoCollection` (deny read/write access to collections while another context holds exclusive access). Change tracking is snapshot based: at load and create the db context captures a per instance baseline of the serialized members; a proxy flags itself a change candidate on mutation (`ChangeTrackingInterceptor`), and non proxy tracked models (created or replaced instances) are always diffed. `SaveChangesAsync` diffs each model against its baseline to compute the changed members, so a diff with no change saves nothing. Loaded models are deduplicated per db context instance (identity map, EF-like): one document materializes one instance inside a scope, references to already loaded documents return the existing instance, and a full load upgrades a loaded summary in place (`MergeFullModel`); deletes and upsert old snapshots evict from the map. The no cache serializer modifier disables both change registration and deduplication.
- **Origin repositories** (hierarchically dependent model types): a db context can declare multiple repositories over the same model type, or over types of the same inheritance chain. The association "reference member → origin repository" is static, configured on the reference serializer with the typed factory (`ReferenceSerializer.Create(engine, config, sourceRepository: (IMyDbContext dbContext) => dbContext.MyRepo)` — generic arguments fully inferred from the selector, source compatibility with the reference model and key types checked at compile time; the cast of the scope to the declared db context type happens once inside the library, failing detailed, and is the natural hook for cross db context resolution on MODM-101); the untyped constructor parameter (`sourceRepository: dbContext => ((IMyDbContext)dbContext).MyRepo`) stays as escape hatch for the one declaration invariance can't express — a base-typed repository sourcing derived-typed references. Selectors are invoked per scope, since repositories are per-scope instances. Reference serializers without `sourceRepository` RESOLVE it at engine build to the single db context repository property compatible by model and key type (`BuildEngine` reads the repository property values off its own builder instance): ambiguity fails fast at startup with a detailed `MongodmAmbiguousRepositoryException`, and references without any compatible repository (models of another db context, pending MODM-101) stay unresolved and unbound. Declared source repositories are validated at engine build (`BuildEngine` invokes the selectors on its own builder instance): a repository not hosting the reference model type, or with a different key type, fails fast with a detailed `MongodmInvalidEntityTypeException`; a typed declaration on a db context type not implemented by the builder fails fast with a detailed `InvalidOperationException`. Binding is member-exact: reference serializers always push their resolved source around the inner deserialization (a null repository shadows the outer operation one when unresolvable), root documents bind the repository reading them (carried by `DbExecutionContextHandler.Repository`), and `ReferenceableInterceptor` binds the current operation repository verbatim — no runtime type deduction. Proxy models created during the schema discovery bind the internal `SchemaDiscoveryRepository` decoy. `IReferenceable.SourceRepository` stays nullable only for cross db context references (annotated on MODM-101). Entity models serialized as full embedded documents (instead of references) are unsupported. Saves, lazy loads and the identity map key on the bound source repository. The dependencies update task fans out to every repository handling a referencing root type.
- **Changes save semantics** (`SaveChangesAsync` → `Repository.SaveChangesAsync`): each changed model is persisted with a single atomic `FindOneAndUpdateAsync` setting only its changed members (`$set`, or `$unset` for members excluded by their serialization options), guarded by the active model map schema id in the update filter — the check is in the filter to be atomic with the update, because setting members serialized with the active schema into a document shaped by an older schema would mix schemas into a broken document. Documents on a not active schema (or with changed members not mapped by the active schema) fall back to a whole document replace that migrates them; repositories can opt into whole document replacement with `RepositoryOptions.SaveWithDocumentReplace`, and direct `ReplaceAsync` calls always replace the whole document (the explicit atomic replace escape hatch). The returned updated document refreshes the saved model in place (`MergeFullModel`): after a save, members not changed locally can update with concurrent changes from other scopes — the save is the synchronization point of the unit of work — and a saved summary upgrades to a full model. Conflict granularity is the member: concurrent changes to disjoint members all survive; changes to the same member (arrays included, e.g. concurrent adds to the same collection member) stay last writer wins — full protection would need optimistic concurrency (proposal tracked separately).
  - `Domain/Models/` — internal operation log entities persisted in the `_db_ops` collection (`SeedOperation`, `DbMigrationOperation` with its `DbMigrationOpAgg/` logs).
- **`src/MongODM.AspNetCore`** (`Etherna.MongODM.AspNetCore`) — DI integration: `AddMongODM` configuration builder, keyed singleton `IDbContextEngine` + scoped `DbContext` registration, `DbDependencies`, execution context wiring.
- **`src/MongODM.AspNetCore.UI`** (`Etherna.MongODM.AspNetCore.UI`) — Admin dashboard as a Razor Pages area (`Areas/MongODM/Pages/Index*`), mapped on a configurable `DashboardOptions.BasePath` and guarded by `IDashboardAuthFilter`s. Static assets are self-contained in `wwwroot/` (no external client libraries); the status/start endpoints are page handlers polled by `wwwroot/js/mongodmDash.js`.
- **`src/MongODM.Hangfire`** (`Etherna.MongODM.Hangfire`, root namespace `Etherna.MongODM.HF`) — `ITaskRunner` implementation scheduling MongODM tasks on Hangfire.
- **`src/MongODM`** (`Etherna.MongODM`) — Meta package wiring the full stack (AspNetCore + Hangfire) with a single `AddMongODMWithHangfire` entry point.
- **`test/MongODM.Core.UnitTests`** — xUnit + Moq unit tests for the core.
- **`test/MongODM.IntegrationTests`** — xUnit integration tests against a real MongoDB instance, pinning end-to-end behavior (change tracking, referenced models and lazy loading, execution scope isolation).
- **`samples/AspNetCoreSample`** — runnable demo app (not packed).

Key cross-cutting points:

- **The MongoDB driver is the Etherna fork** (`Etherna.MongoDB.Driver`, namespaces `Etherna.MongoDB.*`) — never reference the official `MongoDB.*` packages or namespaces.
- **Execution contexts**: `IExecutionContext` (`Etherna.MongODM.Core.ExecContext` namespaces, vendored from the former standalone `ExecutionContext` package) provides ambient per-flow state (HTTP request or async-local scope). `DbExecutionContextHandler` associates the current db operation to the flow, always carrying the `IDbContextEngine` and, when the operation runs inside a scope, also the `IDbContext` (null for engine level work like schema registration); `ExclusiveAccessHandler` marks a flow as owner of an exclusive access. Handlers are `IDisposable` scopes registered in `context.Items`.
- **DbContext engines are singletons, DbContext instances are scoped**: each DI scope (request, Hangfire job) gets its own `DbContext` instance, attached with `AttachToEngine` to the singleton `DbContextEngine` of its type (registered as keyed singleton `IDbContextEngine`, keyed by the db context type). Process-wide state (exclusive access flags, seeding state, registries, connections) lives on the engine; scoped instances own their repositories and unit of work state.
- **Exclusive access locking**: `RunWithExclusiveAccessAsync` (used by seeding and migrations) sets `IsExclusiveReadEnabled`/`IsExclusiveWriteEnabled`; while set, `LimitedAccessMongoCollection` throws `UnauthorizedAccessException` for any flow not holding an `ExclusiveAccessHandler`. Anything that must keep working during a migration (e.g. migration status reads for the dashboard) has to create its own handler scope.
- **Transactions** (`IDbContext.ExecuteInTransactionAsync`): starts a transaction on a new session of the engine and registers it as ambient for the flow with a `DbSessionHandler` scope; `LimitedAccessMongoCollection` enlists in the ambient session every operation invoked without an explicit session on collections of the same engine (LINQ queries included — the LINQ3 provider executes through the public `Aggregate*` methods of the wrapped collection), so repository CRUD, queries and `SaveChangesAsync` inside the callback are transactional. Commit on callback completion, abort on exception, no automatic retry (a retry would replay a unit of work whose in-memory state was already consumed by the first attempt). Scoped to the engine connection: other db contexts (children included) don't enlist; change stream watches and estimated document counts stay session-less (not allowed in transactions); dependencies update tasks enqueued by saves are not transactional (after an abort they converge to the committed state). Requires a replica set or sharded cluster; sessions don't support concurrent operations inside the callback. With `DbContextOptions.EnableTransactionsWithReplicaSet` (default true) and a deployment supporting transactions — detected at runtime from the cluster topology (`IDbContextEngine.SupportsTransactions`: replica set, sharded, or load balanced; standalone servers keep plain saves with no configuration needed) — `SaveChangesAsync` saves its changed models into an implicit transaction of its own (skipped with no changed models, or when a session is already ambient — the saves enlist in it instead of nesting): atomic unit of work flushes without touching application code; set the option false to opt out.
- **`ConfigureAwait(false)` is required** on every awaited call in library code — these are libraries with no synchronization context to preserve.
- **Logging** uses strongly-typed `LoggerMessage.Define` delegates in each project's `Extensions/LoggerExtensions.cs`, grouped by level, with incremental never-reused event ids; the header comment `Last event id is: N` is the source of truth for the next free id — update it when adding a delegate.
- Every source file starts with the standard **LGPL** copyright header (see any existing file); the solution license is GNU LGPL (`COPYING-LESSER`).

## Issue tracker

Bugs and features are tracked in Jira project **MODM** (https://etherna.atlassian.net/projects/MODM). Branch names follow `feature/MODM-<id>-<slug>` / `improve/MODM-<id>-<slug>` / `fix/MODM-<id>-<slug>` — match this when creating branches.

## Release 0.25.0 work line

The 0.25.0 release removes DbCache and rebuilds the db context lifecycle (scoped contexts over singleton engines, per-instance changed models and identity map, member level saves, source repositories). Landed on `dev`: MODM-179, MODM-82, MODM-195, MODM-197; in flight: MODM-49 (origin/source repositories). Every remaining 0.25.0 issue follows the same workflow: branch from `dev` (`improve/MODM-xxx-*` or `feature/MODM-xxx-*`), tests and AGENTS aligned in the same change, commits only after human review, one PR per issue to `dev`.

After the library work, the Etherna services must be migrated: see [SERVICES-MIGRATION.md](SERVICES-MIGRATION.md) for the breaking changes summary, the per-service critical points (captive singleton consumers, root provider resolutions, identity map growth spots, test helper rewrites) and the live integration test checklist gating the release.

# Coding Style

## General Principles

- Keep commits clean: only include changes strictly necessary for the task at hand.
- Keep `README.md` aligned: when a change touches configuration, features, build/run steps, or architecture, update `README.md` in the same change.
- Keep the documentation wiki aligned: user-facing docs live in the sibling `mongodm.wiki` repo (published at https://github.com/Etherna/mongodm/wiki, checked out next to this one). Every substantial change to the public API, configuration, features, or architecture must be reported there in the same work line. The wiki currently documents the 0.25.0 API; see its own `AGENTS.md` for its conventions.
- Never reference AI agents or assistants in commits or code — no agent names, no `Co-Authored-By` agent trailers, no "generated/assisted by" notes. Commit messages and code must read as the team's own work.
- Exceptions to these conventions are accepted when strictly necessary or when they significantly improve code quality. Justify with a comment where needed.
- All elements (usings, properties, methods, fields, enum members, etc.) are always alphabetically ordered within their respective sections.
- Primary constructors are preferred everywhere the constructor is a simple parameter assignment.
- Keep code clean: remove unused variables, dead code, and redundant imports.

## Naming

- **Classes/Structs**: PascalCase (`DbMigrationManager`, `ModelMap`)
- **Interfaces**: `I` prefix (`IDbContext`, `ITaskRunner`)
- **Async methods**: always `Async` suffix (`SaveChangesAsync`, `FindOneAsync`)
- **Properties**: PascalCase, with `Is`/`Has` prefix for booleans (`IsSeeded`, `CurrentStatus`)
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

Public API members are documented with XML doc comments (`///`), with a well-composed description and no superfluous information; document non-public members too whenever it aids understanding.

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
- Don't extract a private helper method for logic used in a single place — inline it. Reserve helpers for code shared by two or more call sites (or when extraction materially clarifies an otherwise long, complex method)
- Framework-initialized components implement `IDbContextEngineInitializable` (engine level: registries, maintainer, migration manager) or `IDbContextInitializable` (scope level: repositories and their registry) — `Initialize(..., logger)` + `IsInitialized` guard instead of taking the dependency in the constructor

### Persisted Model Classes

- `virtual` on all properties and methods for proxy support
- Protected parameterless constructor for deserialization
- Collection encapsulation with a private backing field exposed as `IEnumerable<T>`, never `null` (at most empty)
- `[PropertyAlterer(nameof(MyProp))]` on every method that modifies a property without going through its setter (e.g. mutating a backing field) — the lazy-load trigger for summary reference models. It is NO LONGER needed for change tracking (snapshot based now); it is kept only for the lazy-load role, until MODM-189 replaces it with a source generator. Collections stay encapsulated (read-only interface, non public setter), mutated only through domain methods, so a snapshot diff always detects the change

## Async Patterns

- Always suffix with `Async`
- `CancellationToken cancellationToken = default` as the optional last parameter (non-nullable, `default` — not `CancellationToken? = null`)
- Return `Task` or `Task<T>`, never `async void`
- `ConfigureAwait(false)` on **every** awaited call in library code

## Null Handling

- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- `ArgumentNullException.ThrowIfNull(param)` for parameter validation — no explicit `nameof`, the parameter name is captured automatically
- `is null` / `is not null` (not `== null`)
- Prefer `null` over `default` wherever the type admits it: optional parameter defaults, late-init member initializers (`= null!`, not `= default!`), returns and assignments. Keep `default` only where `null` can't apply: non-nullable value types (e.g. `CancellationToken cancellationToken = default`) and unconstrained generic type parameters.
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
- Prefer collection expressions over constructors to initialize any collection (lists, arrays, dictionaries, etc.): `[]` not `new()`, `["a", "b"]` not `new List<string> { "a", "b" }`. Use a constructor only when a collection expression can't express the intent (e.g. presizing capacity with `new List<T>(capacity)`, or building a specific set type like `new HashSet<T>(value ?? [])`). This applies also where the surrounding legacy code still uses constructors: new code follows the rule, not the neighbors.
- Target-typed `new()` when type is clear from context (for non-collection types)
- Tuple deconstruction for multiple return values
- Prefer a property pattern over a chain of `&&` combining a type/null check with member accesses: it expresses the condition as a single declarative shape the value must match, rather than an imperative sequence of checks
- `field` keyword for field-backed properties (e.g. lazy initialization) instead of an explicit backing field: `public T Prop => field ??= Compute();`. Needs C# 14: in `src/` it applies only once the `net8.0`/`net9.0` targets are dropped (each TFM compiles with its default LangVersion); the net10-only test projects can use it today
- Lock fields: prefer the dedicated `System.Threading.Lock` type (.NET 9+) over a plain `object` — more expressive, and the compiler enforces correct `lock` usage on it. In `src/` it applies only once the `net8.0` target is dropped (lowest-target rule); the net10-only test projects can use it today

## LINQ

- Method syntax preferred over query syntax
- Query syntax only for complex join/groupby with multiple `from` clauses
- Fluent chaining, one operation per line for readability

## Testing (xUnit + Moq)

- AAA pattern with section comments: `// Setup.`, `// Action.`, `// Assert.` (collapse when a single statement covers two phases)
- `[Fact]` for basic tests, `[Theory]` with `[InlineData]`/`[MemberData]` for parameterized cases
- xUnit assertions: `Assert.Equal()`, `Assert.NotNull()`, `Assert.ThrowsAsync<T>()`
- Moq for mocking: `new Mock<IDbContext>()`
- **No `ConfigureAwait` in test code** — inside tests write plain `await foo()`; the library rule applies to `src/` only
- The test project mirrors the `MongODM.Core` layout
