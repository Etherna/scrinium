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

Six source projects, four test projects, one sample (each `src/` project ships a NuGet package, except the generator, packed inside the core one):

- **`src/MongODM.Core`** (`Etherna.MongODM.Core`) — The framework itself, host-agnostic. Main areas:
  - `DbContext.cs` / `IDbContext.cs` / `DbContextEngine.cs` / `IDbContextEngine.cs` — unit of work and its engine, related by composition (`IDbContext` does NOT extend `IDbContextEngine`). `DbContextEngine` owns the scope independent members built once at initialization (connections, schema registries, seeding cache, exclusive access locking with `RunWithExclusiveAccessAsync`), and is the type captured by the serialization pipeline; `DbContext` attaches to its engine (exposed as `IDbContext.Engine`) and owns the current unit of work state (`ChangedModelsList`, `SaveChangesAsync`, per-instance repositories, migration facades, `ExecuteInTransactionAsync`).
  - `Repositories/` — `Repository<TModel, TKey>` with typed access to collections; every collection access passes through `AccessToCollectionAsync`. The indexes defined on a collection (`GetDefinedIndexModelsAsync`, built and pruned by the migration index steps) are the custom ones of `RepositoryOptions.IndexBuilders`, taking the name rendered from their keys when declared without one, plus an automatic sparse index for each id path of the referenced documents: a reference id path opening a custom index — its first key, with an ascending or descending sort order — doesn't get its automatic index, since the custom one already serves the queries on that field, with the configuration chosen by the application. `EstimatedDocumentCountAsync` sizes a collection from its metadata, at constant cost. `CountDocumentsBySchemaIdAsync` groups the collection documents by the model map schema id they carry, resolving it from the current schema id element name or from a read fallback name (documents with no schema id element count aside): schema ids not registered on the db context report too, so the result tells what a migration would have to convert. The grouping can't use an index (the server scans the collection even when the schema id element is indexed), so its cost is linear in the collection size: it belongs to on demand diagnostics, never to a polled or startup path.
  - `Serialization/` — model maps and versioned schemas (`MapRegistry`, `ModelMap`, member maps), discriminator registry, serializers and modifiers. Model map schema ids must be unique across the whole db context, not only inside their model map, with the `fallback` id reserved to fallback schemas: violations fail fast at engine build with a detailed `MongodmDuplicateSchemaIdException`. Documents carry their model map schema id in the `_s` element, configured by `DbContextOptions.ModelMapSchemaId`: reads also recognize the fallback element names (default `_m`), so documents written with a previous name keep deserializing, and migrate to the current name with their next whole document write (a member level save guards on the current name only, falling back to the migrating replace). The mapped id member of every entity model map (reference configurations included) must be the implicit `IEntityModel<TKey>.Id` implementation — the typed id contract addressed by repositories, identity map and references, and the persisted identity, must be the same member: violations fail fast at engine build with a detailed `MongodmInvalidIdMemberException`. Entity ids can use types mapped with a custom serializer map: the map claims its serializer registry slot at registration, so serializer lookups running before maps registration completes (e.g. the driver id generator convention resolving the id member serializer at automap) resolve the custom serializer also for types otherwise served by the driver providers (like Guid, whose driver default serializer fails at use with its unspecified representation); a lookup resolving earlier caches the `MappedSerializerAdapter` fabricated by the serialization provider, delegating every operation to the mapped serializer and kept as the registered serializer at map registry freeze. Reference serializer configurations are separate id spaces (their summary schema ids can mirror the root ones) and stay out of the check. Root model map schemas can declare a post-load fix function (`fixDeserializedModelFunc`), invoked on the root deserialization path only: reference schema configurations build through their own `IReferenceModelMapBuilder` surface, which doesn't accept the hook — a model fix belongs to the origin document's root schemas, and reference members lazy load from the fixed origin document. Reference documents with an unrecognized or missing schema id deserialize through the configured fallback serializer or schema when present, or by default reading only the reference id (any other member lazy loads from the origin document). A model deserialized from a reference document is a summary whose loaded member names derive from the document elements, mapped through the member maps of the schema deserializing it — independent of setter observability, so members set through private or non virtual setters report as loaded too, while a member missing from the document (including one assigned by a specified serializer default value) lazy loads at its first get; only a custom fallback serializer, deserializing without a schema, keeps the proxy-observed setted members as source. Dates are UTC instants persisted as BSON DateTime: every persisted `DateTimeOffset` member of the internal models (migration operation and logs) sets `DateTimeOffsetSerializer(BsonType.DateTime)` on its member map (offset and sub-millisecond precision discarded; the driver serializer reads any other driver representation too, keeping its original offset). Model timestamps are `DateTimeOffset` values generated with `UtcNow`. There is no registry-wide default: application models configure their own `DateTimeOffset` members the same way (unconfigured members persist with the driver default, the `{DateTime, Ticks, Offset}` document).
  - `ProxyModels/` — source generated model proxies (emitted at compile time by `src/MongODM.Core.Generators`, one sealed `XProxy` subclass per concrete entity model, registered with assembly level `GeneratedProxyModelAttribute`) enabling lazy loading of referenced models (`IReferenceable`). Change tracking is snapshot based and lives on the db context, not on a proxy interface: a proxy only flags itself a change candidate on mutation (through the generated member overrides), so non proxy models are trackable too. Proxy types have no registered model maps: serializers purge a proxy instance to its concrete model type at lookup and serialize it through that type's class map as the nominal type (`SerializeAsNominalType`). Implicit lazy loads are synchronous over the db call and honor `DbContextOptions.ImplicitLazyLoad`: load logging a deduplicated warning (default), load silently, or deny throwing `MongodmLazyLoadingException`. Performance sensitive code preloads explicitly with `IDbContext.LoadValuesAsync` (per instance, or batched with a single `$in` query per source repository — members are a no-op precondition, any load is of the whole document, merged in place through the identity map) and inspects with `IsMemberLoaded`. A document can change its concrete type over time, keeping its id: a full load finding a type not matching the loaded instance registers the fresh instance as the loaded one (returned by that and every next load) and invalidates the outdated instance, whose runtime type can't upgrade — any application interaction with it throws a detailed `MongodmOutdatedModelTypeException` (inspect with `IDbContext.IsOutdatedModel`), while the library internals keep reading it under change tracking suppression. The id member is never proxied (resolved as the implementation of the typed entity model interface id, with the `Id` name as convention fallback; at runtime, as the mapped id member of the active schema): identity is definitionally present on any instance — never joining the summary member names, always reported loaded, skipped by the generated merges — and stays readable also on an outdated instance, where it drives the reload.
  - `Migration/` — `DocumentMigration` scripts between document schemas. Documents scan raw, deserializing each one apart (a typed cursor can't survive a document failing deserialization): a document failing deserialization or processing is skipped, keeping its current content, and reported into the result errors with its id and error (detail capped at `DocumentMigration.MaxTrackedDocumentErrors`, with the full count aside), while every other document migrates — `MigrationResult.MigratedDocuments` counts the documents processed without errors, `ProcessedDocuments` the whole scan (migrated plus failing ones). A run can ask to abort at its first failing document instead (`stopAtFirstError`), and can execute as a **dry run**: the processor executes under an ambient `DryRunHandler` (collection writes execute only their client side — definition rendering and document serialization — returning simulated results without touching the server; writes without a client side half, index management and aggregate to collection, throw).
  - `Tasks/` — background tasks invoked through `ITaskRunner` (`UpdateDocDependenciesTask` propagates updated summaries to referencing documents, reserializing each sub-document with its reference member serializer — reference schema shape, schema id, and discriminator of the current referenced model type — and writing server side with one `UpdateMany` per hosting repository and reference id path, without per document round trips nor content read back; `MigrateDbContextTask` runs a db context migration under exclusive access — except dry run operations, running without the lock since they persist nothing).
  - `Utility/` — `DbMaintainer` (enqueues dependency updates on model changes, only when the changed members resolve at least one reference id member map: without involved reference members the task would have nothing to propagate, so the enqueue is skipped), `DbMigrationManager` (drives db context migrations and their `DbMigrationOperation` log: any failure, unhandled exceptions included, marks the operation failed — never left on running status, which would block every future migration — logs at error level, and throws only when the caller asks for errors; failing documents don't abort an operation, that completes its scan and closes failed with their detail, unless started with `TryStartMigrationAsync(stopAtFirstError: true)` (`DbMigrationOperation.IsStopAtFirstErrorEnabled`); a dry run operation, `DbMigrationOperation.IsDryRun` started with `TryStartMigrationAsync(dryRun: true)`, skips the index steps and simulates the document migrations, persisting only the operation log with the failing documents detail), `ExclusiveAccessHandler` + `LimitedAccessMongoCollection` (deny read/write access to collections while another context holds exclusive access), `DryRunHandler` (marks a flow whose collection writes are simulated; `DbMaintainer` skips the dependencies propagation under it). Change tracking is snapshot based: at load and create the db context captures a per instance model document (the serialized members); a proxy flags itself a change candidate on mutation (through the generated member overrides), and non proxy tracked models (created or replaced instances) are always diffed. `SaveChangesAsync` diffs each model against its model document to compute the changed members, so a diff with no change saves nothing. Loaded models are deduplicated per db context instance (identity map, EF-like): one document materializes one instance inside a scope, references to already loaded documents return the existing instance, a full load upgrades a loaded summary in place (`MergeFullModel`), and a full load finding the document with another type of its hierarchy replaces the loaded instance with the fresh one, invalidating the outdated instance (`ReplaceOutdatedLoadedModel`); deletes and upsert old snapshots evict from the map. The no cache serializer modifier disables both change registration and deduplication.
- **Origin repositories** (hierarchically dependent model types): a db context can declare multiple repositories over the same model type, or over types of the same inheritance chain. The association "reference member → origin repository" is static, configured on the reference serializer with the typed factory (`ReferenceSerializer.Create(engine, config, sourceRepository: (IMyDbContext dbContext) => dbContext.MyRepo)` — generic arguments fully inferred from the selector, source compatibility with the reference model and key types checked at compile time; the declared db context type resolves once inside the library — the current scope db context when it implements it, or the child db context attached to the scope otherwise — failing detailed when neither applies); the untyped constructor parameter (`sourceRepository: dbContext => ((IMyDbContext)dbContext).MyRepo`) stays as escape hatch for the one declaration invariance can't express — a base-typed repository sourcing derived-typed references — and stays same-db-context only. Selectors are invoked per scope, since repositories are per-scope instances. Reference serializers without `sourceRepository` RESOLVE it at engine build to the single db context repository property compatible by model and key type (`BuildEngine` reads the repository property values off its own builder instance): ambiguity fails fast at startup with a detailed `MongodmAmbiguousRepositoryException`, and references without any compatible repository fail fast with a detailed `InvalidOperationException` pointing to the cross db context declaration — every reference of a built engine binds a source repository, so the identity map lookups are always by repository. Declared source repositories are validated at engine build (`BuildEngine` invokes the selectors on its own builder instance): a repository not hosting the reference model type, or with a different key type, fails fast with a detailed `MongodmInvalidEntityTypeException`. **Cross db context references**: a typed declaration on a db context type not implemented by the builder declares its source on another db context — the declared type must be reachable through exactly one child db context type of the options (`DbContextOptions.ParentFor`), validated at engine build with a detailed `InvalidOperationException` when missing or ambiguous (the selector can't run on the builder, so the compile time check of the typed factory is the compatibility guarantee). At deserialization the source resolves on the child db context instance attached to the scope, and the referenced model homes there entirely: identity map, model document and change tracking live on the child db context (one instance per document also across the parent and child contexts of the scope), lazy loads run through the child repository, mutations persist with the child save — cascaded by the parent `SaveChangesAsync` — and new referred models (null id) auto create into the child repository (out of the parent implicit save transaction, being another engine). The dependencies update propagation stays per-engine: denormalized summary members of cross db context references don't auto update when the referenced model changes, so keep them id-only unless staleness is acceptable. Binding is member-exact: reference serializers always push their resolved source around the inner deserialization, paired with the db context owning it (a null repository shadows the outer operation one when unresolvable), root documents bind the repository reading them (carried by `DbExecutionContextHandler.Repository`), and proxy models bind the current operation repository verbatim at creation — no runtime type deduction. `IReferenceable.SourceRepository` is NOT nullable: every proxy materializes inside an operation addressing a collection, so `ProxyGenerator.CreateInstance` binds its source repository or fails fast with a detailed `InvalidOperationException` — an instance unable to save or lazy load is a broken flow, not a supported state. The db context binding stays nullable instead, since the no cache serializer modifier deliberately clears it to keep read only massive scans out of the unit of work. Entity models serialized as full embedded documents (instead of references) are unsupported: the maps freeze detects every member serializing an entity model type through a class map, reference configurations included, and fails fast with a detailed `MongodmEmbeddedEntityModelException` — at most, a reference can denormalize every member, staying a reference with its own source. A custom serializer, set on the member or mapped for the type, never enters the document serialization pipeline: the opt-out for value-object-like models. Saves, lazy loads and the identity map key on the bound source repository. The dependencies update task fans out to every repository handling a referencing root type.
- **Changes save semantics** (`SaveChangesAsync` → `Repository.SaveChangesAsync`): each changed model is persisted with a single atomic `FindOneAndUpdateAsync` setting only its changed members (`$set`, or `$unset` for members excluded by their serialization options), guarded by the active model map schema id in the update filter — the check is in the filter to be atomic with the update, because setting members serialized with the active schema into a document shaped by an older schema would mix schemas into a broken document. Documents on a not active schema (or with changed members not mapped by the active schema) fall back to a whole document replace that migrates them; repositories can opt into whole document replacement with `RepositoryOptions.SaveWithDocumentReplace`, and direct `ReplaceAsync` calls always replace the whole document (the explicit atomic replace escape hatch). The returned updated document refreshes the saved model in place (`MergeFullModel`): after a save, members not changed locally can update with concurrent changes from other scopes — the save is the synchronization point of the unit of work — and a saved summary upgrades to a full model. Conflict granularity is the member: concurrent changes to disjoint members all survive; changes to the same member (arrays included, e.g. concurrent adds to the same collection member) stay last writer wins — full protection would need optimistic concurrency (proposal tracked separately). **New referred models auto create**: an entity model referred with a null id is a new model, and every repository write serializing references (create, member level save, whole document replace) creates it into its source repository first. A discovery pass serializes the persisting model (the changed members for a member level save) under an ambient collector reported by the reference serializers; the ids of the persisting and discovered models are assigned upfront through the mapped id generator, so references between new models serialize complete in any creation order, cycles included; each discovered model then inserts with its change tracking started, without flushing the unit of work (inside a member level save, the inserts enlist in the implicit save transaction). A new referred model whose reference member doesn't resolve a source repository, or whose id member has no id generator, fails the write with a detailed `InvalidOperationException`; and serializing a reference to a model without id outside a discovery pass (upserts and raw collection writes, which skip the auto creation) throws too — a null id reference deserializes to null, so it never persists silently. Reference documents without id already persisted keep deserializing to null.
  - `Domain/Models/` — internal operation log entities persisted in the `_db_ops` collection (`SeedOperation`, `DbMigrationOperation` with its `DbMigrationOpAgg/` logs).
- **`src/MongODM.Core.Generators`** (`Etherna.MongODM.Core.Generators`) — Roslyn incremental source generator emitting the proxy model types (netstandard2.0, the required target for compiler extensions). Packed inside the `Etherna.MongODM.Core` package under `analyzers/dotnet/cs`, so every consumer referencing the package generates proxies for its own entity models with no setup; in-repo projects defining models (core, tests, sample) reference it explicitly with `OutputItemType="Analyzer"`, since analyzers don't flow through project references. Inspect the emitted sources with `-p:EmitCompilerGeneratedFiles=true` (under `obj/GeneratedFiles`).
- **`src/MongODM.AspNetCore`** (`Etherna.MongODM.AspNetCore`) — DI integration: `AddMongODM` configuration builder, keyed singleton `IDbContextEngine` + scoped `DbContext` registration, `DbDependencies`, execution context wiring.
- **`src/MongODM.AspNetCore.UI`** (`Etherna.MongODM.AspNetCore.UI`) — Admin dashboard as a Razor Pages area (`Areas/MongODM/Pages/Index*`), mapped on a configurable `DashboardOptions.BasePath` and guarded by `IDashboardAuthFilter`s. Static assets are self-contained in `wwwroot/` (no external client libraries); the status/start endpoints are page handlers polled by `wwwroot/js/mongodmDash.js`. The state changing POST handlers keep the default Razor Pages antiforgery validation: the page renders the token, and the script sends it back as the `RequestVerificationToken` header of every POST, while the GET status handlers stay token free. Everything renders as one card per db context: migrations can start also as dry run, or asked to stop at their first failing document, and operations render their flags and failing documents. Each card also carries a model schemas section, listing per collection the schema ids its documents can carry — the active and secondary schemas of the concrete model types assignable to the repository model type, since a document carries the active schema id of its own concrete type (fallback schemas stay out, their reserved id is never written). The page renders this from the maps alone, without touching the database; expanding the section sizes each collection with the estimated count (constant cost), and the linear cost schema ids count runs only on the explicit per collection request, adding a row for each schema id found on documents but not registered, and one for documents with no schema id: a non zero count outside the active schemas is what a migration has to convert. Read-only db contexts render without migration controls, schemas section included (counting is a read).
- **`src/MongODM.Hangfire`** (`Etherna.MongODM.Hangfire`, root namespace `Etherna.MongODM.HF`) — `ITaskRunner` implementation scheduling MongODM tasks on Hangfire.
- **`src/MongODM`** (`Etherna.MongODM`) — Meta package wiring the full stack (AspNetCore + Hangfire) with a single `AddMongODMWithHangfire` entry point.
- **`test/MongODM.Core.UnitTests`** — xUnit + Moq unit tests for the core.
- **`test/MongODM.Core.Generators.UnitTests`** — xUnit tests running the generator in-memory with `CSharpGeneratorDriver`: selection rules, emitted shape, altered members computation, and the compile check of the generated sources.
- **`test/MongODM.AspNetCore.UI.UnitTests`** — xUnit + Moq tests of the admin dashboard, hosted in-memory with `TestServer` over mocked db contexts: antiforgery validation of the page handlers.
- **`test/MongODM.IntegrationTests`** — xUnit integration tests against a real MongoDB instance, pinning end-to-end behavior (change tracking, referenced models and lazy loading, execution scope isolation).
- **`samples/AspNetCoreSample`** — runnable demo app (not packed).

Key cross-cutting points:

- **The MongoDB driver is the Etherna fork** (`Etherna.MongoDB.Driver`, namespaces `Etherna.MongoDB.*`) — never reference the official `MongoDB.*` packages or namespaces. Fork >= 3.10.1 is required: proxy model serialization relies on `BsonClassMapSerializer` honoring `SerializeAsNominalType`, a fork-only behavior proposed upstream (CSHARP-3153) and pinned by the `DriverSerializationBehaviorTest` unit test — it must stay green on every driver bump.
- **Execution contexts**: `IExecutionContext` (`Etherna.MongODM.Core.ExecContext` namespaces, vendored from the former standalone `ExecutionContext` package) provides ambient per-flow state (HTTP request or async-local scope). `DbExecutionContextHandler` associates the current db operation to the flow, always carrying the `IDbContextEngine` and, when the operation runs inside a scope, also the `IDbContext` (null for engine level work like schema registration); `ExclusiveAccessHandler` marks a flow as owner of an exclusive access. Handlers are `IDisposable` scopes registered in `context.Items`.
- **DbContext engines are singletons, DbContext instances are scoped**: each DI scope (request, Hangfire job) gets its own `DbContext` instance, attached with `AttachToEngine` to the singleton `DbContextEngine` of its type (registered as keyed singleton `IDbContextEngine`, keyed by the db context type). Process-wide state (exclusive access flags, seeding state, registries, connections) lives on the engine; scoped instances own their repositories and unit of work state.
- **Exclusive access locking**: `RunWithExclusiveAccessAsync` (used by seeding and migrations) sets `IsExclusiveReadEnabled`/`IsExclusiveWriteEnabled`; while set, `LimitedAccessMongoCollection` throws `UnauthorizedAccessException` for any flow not holding an `ExclusiveAccessHandler`. Anything that must keep working during a migration (e.g. migration status reads for the dashboard) has to create its own handler scope. Dependencies update tasks never hold a handler: executed during an exclusive access they fail, and the task executor retry converges them on the post exclusive state — background propagation never interleaves with migrations.
- **Read-only access**: `DbContextOptions.IsReadOnly` denies any write from the whole db context, `RepositoryOptions.IsReadOnly` from a single repository (`IRepository.IsReadOnly` reports the effective flag, and repositories can coexist with different flags on the same db context). Useful to consume collections owned by another application. Enforcement lives on `LimitedAccessMongoCollection` and its index manager wrappers (`LimitedAccessMongoIndexManager`, `LimitedAccessMongoSearchIndexManager`): every write operation on a read-only collection throws `UnauthorizedAccessException`, index and search index management included (creations, drops and updates are writes; listings stay reads), while reads work normally. A read-only db context also skips seeding (`SeedIfNeededAsync` logs and returns false) and denies migrations (`TryStartMigrationAsync` returns null, executing throws); migrations of a writable db context skip the index steps of its read-only repositories, whose indexes belong to the collection owner. The admin dashboard renders a read-only db context as a static card with a `Read-only` badge, without migration controls or status polling.
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

Longer explanations of non-obvious behavior use `/* ... */` blocks. Comment only what helps a future reader: non-obvious behavior, intent, or a gotcha. Do **not** write narration of your own reasoning or decisions — that belongs in the commit message / PR description, never in committed code. Comments and docs describe the **present** state only: never reference removed or replaced concepts ("legacy X", "previously done by Y") — git history already tells that story.

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

// Internals.
Task IInternalContract.DoInternalAsync() { ... }

// Helpers.
private void InternalHelper() { ... }
```

Internal facing members (explicit implementations of internal interfaces, internal methods) never mix with the public ones: they go in their own `// Internals.` section, after the public methods and before the helpers.

## Class Design

- `internal sealed` for implementations that aren't part of the public API
- Primary constructors everywhere the constructor is a simple assignment
- Don't extract a private helper method for logic used in a single place — inline it. Reserve helpers for code shared by two or more call sites (or when extraction materially clarifies an otherwise long, complex method)
- Framework-initialized components implement `IDbContextEngineInitializable` (engine level: registries, maintainer, migration manager) or `IDbContextInitializable` (scope level: repositories and their registry) — `Initialize(..., logger)` + `IsInitialized` guard instead of taking the dependency in the constructor

### Persisted Model Classes

- `virtual` on all properties and methods for proxy support
- Protected parameterless constructor for deserialization
- Collection encapsulation with a private backing field exposed as `IEnumerable<T>`, never `null` (at most empty)
- No manual lazy-load annotations: the proxy models source generator analyzes each domain method body and computes the properties it alters (direct backing field accesses included, following non virtual helpers), triggering the full load when such a method runs on a summary model. A method without analyzable source (compiled cross-assembly bases) conservatively full loads on summaries. Collections stay encapsulated (read-only interface, non public setter), mutated only through domain methods, so a snapshot diff always detects the change
- Prefer immutable exposure. A getter that hands out mutable state (a `List<T>`/`Dictionary<>`, or a complex value with public setters or business methods) is legal, but **reading it flags the model for a diff at save** — a change could otherwise escape interception. Expose collections read-only and make embedded value objects immutable (records, get/init-only, no mutating methods) to keep reads free; entity references never count (tracked on their own repository). `ProxyModels/MutabilityAnalyzer` computes this; casting a read-only collection back to mutable to bypass it is unsupported

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
- Prefer a property pattern over a chain of `&&` combining a type/null check with member accesses: it expresses the condition as a single declarative shape the value must match, rather than an imperative sequence of checks. This applies also to pure boolean member chains on the same value, with no type/null check involved: `field is { IsInitOnly: false, IsLiteral: false }`, not `!field.IsInitOnly && !field.IsLiteral`
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
