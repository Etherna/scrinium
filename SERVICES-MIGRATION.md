# Etherna services migration to MongODM 0.25.0

Guide for migrating the Etherna services (etherna-index, etherna-sso, etherna-gateway, etherna-credit, beehive) to MongODM 0.25.0. Sources: full code scan of every service (2026-07-16, service branches current at that date — line references may drift) plus the 0.25.0 development history. **bee-turbo is archived: ignore it.**

## Breaking changes summary (all services)

- **Db contexts are SCOPED**: `AddDbContext` registers a keyed singleton `IDbContextEngine` per context type, plus a scoped `DbContext` instance per DI scope. Every consumer must be scoped/transient/per-request. A singleton receiving a db context in its constructor is a captive dependency: with scope validation it throws at first resolve, without it a context (and its identity map) is silently pinned for the process lifetime.
- **`IDbContext` no longer extends `IDbContextEngine`**: engine members (`Client`, `Database`, `MapRegistry`, `ProxyGenerator`, `ExecutionContext`, `Options`, `SerializerRegistry`, exclusive access, `StartSessionAsync`, `GetMongoCollection`, `DbMaintainer`, `DbMigrationManager`...) moved behind `IDbContext.Engine`. Scope surface: repositories, `DbOperations`, `ChangedModelsList`, `SaveChangesAsync`, loaded/changed model registration APIs, seed/migration facades.
- **`DbCache`, `ClearCache`, `LoadedModels` removed**: replaced by the per-instance identity map (see semantics below). `LoadedModelsTracker` (interim API) also removed.
- **`DbContext.Initialize(...)` and `IDisposable` on `DbContext` removed**: bootstrap is `BuildEngine` (owned by the caller) + `AttachToEngine`. See `test/MongODM.Core.UnitTests/DbContextTest.cs` for the standalone/test pattern.
- **`IModelMapsCollector.Register(IDbContextEngine)`**: collectors receive the engine, not the context.
- **`fixDeserializedModelFunc` signature**: `Func<IDbContext, TModel, Task<TModel>>` — receives the current scope as first parameter. Closures capturing the old `Register` context parameter must migrate (etherna-index `UnsuitableReportMap` does this).
- **ExecutionContext library vendored**: namespaces are `Etherna.MongODM.Core.ExecContext[.AsyncLocal]`; the standalone `Etherna.ExecContext` package is gone.
- **Reference serializers can declare their source repository**: `ReferenceSerializer.Create(engine, config, sourceRepository: (IMyDbContext dbContext) => dbContext.MyRepo)` (typed factory, source compatibility checked at compile time; the untyped constructor parameter stays for base-typed repositories sourcing derived-typed references). Only required when a db context declares multiple repositories over the same model type hierarchy; undeclared sources resolve at startup to the single compatible repository property (ambiguity fails fast). Cross-context references (index → `UserSharedInfo`) stay undeclared and unbound, as today, until MODM-101. Entity models serialized as FULL embedded documents (instead of references) are unsupported: reference them.
- **`Repository.DeleteManyAsync(filter)`** (raw bulk delete) and **`Repository.SaveChangesAsync(model)`** (member-level save) added; `RepositoryOptions.SaveWithDocumentReplace` opts a repository into whole-document replacement on save.
- **`DateTimeOffset` instead of `DateTime` on model timestamps**: the migration operation/log members are `DateTimeOffset` values generated with `UtcNow`. `IEntityModel` no longer declares `CreationDateTime`: the member is application-owned (services' own base declarations keep compiling). Migration rule for each service `CreationDateTime` member: where every usage is replaceable by extracting the creation instant from the `ObjectId` id (`ObjectId.CreationTime`, as the MongODM dashboard does), replace them and drop the persisted member; a member never read at all is simply removed from the model; a member truly needed as data (non-`ObjectId` ids, or precision beyond the id's seconds) becomes `DateTimeOffset` with `DateTimeOffsetSerializer(BsonType.DateTime)` set on its member map (sso's configuration on `UserSharedInfo.LockoutEnd` is exactly this pattern and stays as is), otherwise the wire format changes from `ISODate` to the driver default `{DateTime, Ticks, Offset}` document. Stored `ISODate` values keep reading unchanged.

## Runtime semantics changes (behavioral, no compile error)

- **Identity map per scope (EF-like)**: one document materializes one instance inside a scope; references to already loaded documents return the existing instance (additive summary merge); a full load upgrades a loaded summary in place; `FindOneAsync` by id reads through loaded full instances without a db round trip. **Fresh data = new DI scope.** A document deleted behind the scenes keeps being returned by same-scope finds (documented contract; `UnregisterLoadedModel` is the escape hatch).
- **Save is the synchronization point**: `SaveChangesAsync` updates only the changed members with one atomic statement (guarded by the active schema id: older-schema documents fall back to a migrating whole replace) and refreshes each saved model with the returned document state — members not changed locally can update with concurrent changes from other scopes. Conflict granularity is the member: disjoint concurrent changes all survive; same-member (arrays included) stays last-writer-wins.
- **The no-cache serializer modifier** disables change registration AND identity map participation for the models it deserializes: keep using it for massive read-only scans.
- **Document schema id element renamed `_m` → `_s`**: documents now carry the model map schema id in the `_s` element (`DbContextOptions.ModelMapSchemaId`). Existing documents with `_m` keep reading through the read fallback element names (default `["_m"]`) and migrate to `_s` with their next whole document write — a member-level save on an `_m` document falls back to the migrating replace. No service queries `_m` directly (verified by code scan); the persistence test fixtures with `_m` documents keep passing unchanged.
- **Domain events handlers get their OWN scope**: `EventDispatcher` (Etherna.DomainEvents) is a singleton dispatching handlers in a fresh DI scope. Pre-0.25 handlers shared the singleton context with the triggering flow; post-0.25 they get a separate scoped context reading post-save state. Verify event-driven flows on index/gateway/credit.
- **Hangfire**: per-job DI scope comes from the Hangfire.AspNetCore activator; the per-job async-local context from the `AsyncLocalContextHangfireFilter` registered globally by `AddMongODMWithHangfire`. Nothing to change app side.
- **"MODM-83" create-then-refind workarounds keep working identically** (created models are not proxies and don't enter the identity map; the refind materializes the tracked proxy).

## Per-service critical points

### etherna-gateway (most critical)
- **Four SINGLETON services capture db contexts** (`EthernaGateway.Services/ServiceCollectionExtensions.cs:49-53`): `ConfigurationService`, `PostageBatchService`, `SwarmResourceService`, `UserService` → make them scoped.
- **Concrete bug once contexts diverge**: `GatewayApiHandler.cs:407-411` (`RequestWelcomePackAsync`) loads the user through the singleton `UserService` context and saves with the handler's scoped context → the change would land in the wrong changed-models list and never persist. Same family: `postageBatch.Owner != user` comparisons at `GatewayApiHandler.cs:131` and `SwarmApiHandler.cs:153` with instances from different contexts.
- `GarbageCollectPinnedResourcesTask` iterates all bee pins in one job scope: wrap reads in the no-cache modifier.

### beehive
- **`BeeNodeLiveManager` is a SINGLETON with a db context and a 10s Timer** (`Beehive.Services/Utilities/BeeNodeLiveManager.cs:40-41` + `ServiceCollectionExtensions.cs:54`), resolved from the ROOT provider at startup (`Extensions/ApplicationBuilderExtensions.cs:28`): **the app won't start** until it stops capturing a scoped context (create a scope per heartbeat cycle).
- `PinChunksTask` traverses whole pin trees calling `TryFindOneAndAddToSetAsync` per chunk outside the no-cache modifier: identity map growth on big pins → extend the no-cache usage.
- `BeehiveChunkStore` read paths already use `noCache: true` — semantics preserved. `PushChunksBackgroundService` already creates a scope per work cycle — correct pattern.

### etherna-index
- **`CreateElasticIndexes` resolves `IElasticSearchService` (transitively a db context) from the ROOT provider** (`Extensions/ApplicationBuilderExtensions.cs:29-30`): startup throw → use `CreateScope()`.
- `VideoManifestValidatorTask.cs:65` `DbCache.ClearCache()` → delete the call (each Hangfire job now gets a fresh scoped context; the workaround is obsolete).
- Elastic reindex tasks (`RebuildElasticIndexesTask`, `ReindexElasticDocumentsTask`) cursor over the whole videos collection (+ up to 10k comments per video) in one scope: run reads under the no-cache modifier.
- `UnsuitableReportMap.cs:58-65`: the fix function must use the new signature (`(dbContext, model) => ... ((IIndexDbContext)dbContext).Videos...`) instead of capturing the `Register` parameter.

### etherna-sso
- **No captive singletons** (Duende SystemStore repositories use the raw driver — safe).
- The Identity lockout flow (`UserStore.cs:370-384` + `:455-464`) relies on child-context cascade + identity map dedup of `UserSharedInfo`: covered by mongodm integration tests (`ChildDbContextsTests`), verify live.
- `ClientAppStore` (scoped, wrapped by the Duende client-store cache, runs on non-request threads with its own `InitAsyncLocalContext`): verify scope + execution context on background threads.
- `RoleDelete.cshtml.cs:57-66` loads every user of a role in one scope (bounded, watch it).

### etherna-credit (cleanest)
- No captive consumers. Balance mutations are DB-atomic (`AccessToCollectionAsync` + `FindOneAndUpdateAsync`) — insulated. A `UserBalance` loaded and then atomically incremented in the same scope stays stale in memory (same as with the old DbCache; no regression).
- Verify the DI lifetime of the two `AddDomainEvents` handlers receiving `ISharedDbContext`.

### Cross-cutting (all services)
- **`DbContextMockHelper` in every test project must be rewritten**: `IDbDependencies.DbCache` setup and `dbContext.Initialize(...)` no longer exist. Reference pattern: `test/MongODM.Core.UnitTests/DbContextTest.cs` (build engine from mocked dependencies + `AttachToEngine`; `DisableCreationWithProxyTypes` now sits on the engine's proxy generator).
- Scope validation: enable `ValidateScopes` also outside Development during the migration, to surface captive dependencies immediately.

## Live integration test checklist (before releasing app bumps)

- gateway: welcome pack request; dilute/top-up postage batch; resources offer/defund flows.
- beehive: startup; pin of a large tree; bzz read path; push queue background cycles.
- index: startup (elastic indexes init); video creation + manifest validation end-to-end; comment creation (elastic event handler); reindex tasks.
- sso: login (lockout counters via child cascade); web2/web3 registration; client store under load; alpha pass flow.
- credit: deposit/withdraw; admin balance update; OPS logs.
- all: one summary-changing edit to watch `UpdateDocDependenciesTask` refresh denormalized references; Hangfire maintenance queues draining.
