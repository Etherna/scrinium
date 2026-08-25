# MongODM

[![MongODM on NuGet](https://img.shields.io/nuget/v/MongODM?label=MongODM)](https://www.nuget.org/packages/MongODM/)
[![MongODM.Core on NuGet](https://img.shields.io/nuget/v/MongODM.Core?label=MongODM.Core)](https://www.nuget.org/packages/MongODM.Core/)
[![MongODM.AspNetCore on NuGet](https://img.shields.io/nuget/v/MongODM.AspNetCore?label=MongODM.AspNetCore)](https://www.nuget.org/packages/MongODM.AspNetCore/)
[![MongODM.AspNetCore.UI on NuGet](https://img.shields.io/nuget/v/MongODM.AspNetCore.UI?label=MongODM.AspNetCore.UI)](https://www.nuget.org/packages/MongODM.AspNetCore.UI/)
[![MongODM.Hangfire on NuGet](https://img.shields.io/nuget/v/MongODM.Hangfire?label=MongODM.Hangfire)](https://www.nuget.org/packages/MongODM.Hangfire/)
[![Target frameworks](https://img.shields.io/badge/.NET-8%20%7C%209%20%7C%2010-512BD4)](#supported-frameworks)
[![License: LGPL-3.0](https://img.shields.io/badge/license-LGPL--3.0-blue)](COPYING.LESSER)

**MongODM** is an **ODM framework** (Object-Documental Mapper) for **MongoDB** on .NET, oriented to
ASP.NET Core applications. It maps domain objects to documents and takes care of the hard parts of a
documental data layer: **denormalized references** between documents with **automatic dependency
updates**, **versioned document schemas** coexisting in one collection, and **data migrations**.

MongODM is a set of **libraries**, not an application. Add it to your project to model a plain domain
and get denormalized-document efficiency without maintaining the denormalization by hand.

> **Status.** MongODM is pre-beta: new features are still being added and the public interface can
> change between minor versions. Active use in production is discouraged at this stage.

## Contents

- [Why MongODM](#why-mongodm)
- [Features](#features)
- [Packages](#packages)
- [Installation](#installation)
- [Quick start](#quick-start)
  - [1. Model your domain](#1-model-your-domain)
  - [2. Map the models](#2-map-the-models)
  - [3. Declare a db context](#3-declare-a-db-context)
  - [4. Register it](#4-register-it)
  - [5. Use the repositories](#5-use-the-repositories)
  - [Reference another entity, keep it in sync](#reference-another-entity-keep-it-in-sync)
- [Documentation](#documentation)
- [Supported frameworks](#supported-frameworks)
- [Building and testing](#building-and-testing)
- [Project layout](#project-layout)
- [Package repositories](#package-repositories)
- [Contributing](#contributing)
- [Issue reports](#issue-reports)
- [Questions? Problems?](#questions-problems)
- [License](#license)

## Why MongODM

Document databases are efficient because they let you **denormalize**: store together what you read
together, and get a whole aggregate with a single read, without joins. That efficiency is usually paid
for at the application layer, where the same three problems keep coming back:

- related models live in different documents, so logical joins end up performed by the application,
  with several queries on the database;
- a denormalized value that changes has to be traced and updated in every document that copied it;
- when requirements evolve, every stored document must be migrated to the new schema, or the code
  decorated with explicit checks and conditions on loaded data.

For these reasons document databases are often used as a plain CRUD storage layer, or dropped in favor
of SQL as soon as the domain gets complex. MongODM removes that price: you model a plain domain,
declare how it serializes, and the framework keeps denormalized copies in sync, serves versioned
schemas side by side, and migrates documents on your terms — bringing the efficiency of denormalized
documents to complex application domains.

## Features

- **Denormalized references** — store a related entity as a compact summary inline in the referencing
  document, with fine-grained control over which members are denormalized. The entity itself is stored
  once, in its own collection.
- **Automatic dependency updates** — change a denormalized member and every document referencing it is
  refreshed in background, server side and in bulk, without per-document round trips.
- **Transparent lazy loading** — members left out of a summary load from the origin document at their
  first read; configurable per context (warn, silent, or deny), with explicit asynchronous batch
  preloading for performance-sensitive code.
- **Auto-creation of new referred models** — link a model never persisted before and it is created into
  its repository at save time, with complete references, cycles included.
- **Unit of work with identity map** — repositories are organized under database contexts; inside a
  scope one document materializes one instance, and changes are tracked by member.
- **Member-level saves** — each changed model is persisted with a single atomic update writing only its
  changed members, so concurrent changes to disjoint members all survive.
- **ACID transactions** — automatic enlistment of repository operations, plus an implicit transaction
  around each unit of work flush on deployments supporting them.
- **Versioned document schemas** — several schema versions coexist in the same collection, each document
  recording the schema that wrote it. Documents written with the previous, deprecated name of that
  element keep loading through it, and the admin dashboard counts and migrates them, collection by
  collection.
- **Data migrations** — configurable migration scripts between document schemas, skipping and reporting
  the failing documents, with a **dry run** mode simulating the migration without persisting anything.
  Migrations and seeding are serialized across every application instance connected to the database,
  through a server side lock with an expiring lease (persisted in the `_db_lock` collection, renamable
  with `DbContextOptions.DbLockCollectionName`). The lease is renewed while the work runs, so its
  duration is how long a dead instance blocks the others: each operation chooses it, with
  `TryStartMigrationAsync(lockLeaseDuration)` and `SeedIfNeededAsync(lockWaitTimeout, lockLeaseDuration)`
  (forwarded by the startup `SeedDbContexts`), 10 minutes by default. An unspecified seeding wait for
  the lock owner defaults to the lease duration of that seeding. In process, the exclusive window of a
  migration or seeding also waits for the operations already in flight against the collections before
  starting its work (`DbContextOptions.ExclusiveAccessDrainTimeout`, 5 minutes by default), so it never
  runs beside an operation admitted a moment before it opened.
- **Resource locks** — the same server side lease lock, usable by applications on their own resources:
  exclusive or shared locks on arbitrary resource ids, each inside its own namespace, acquired in one
  call on the db context (`TryAcquireResourceLockAsync`, exclusive by default or shared by parameter)
  with a single atomic command, and renewed in background with a lost lease signal. A dead holder expires
  alone, and a TTL index collects the abandoned lock documents, with no cleanup task to run.
- **Customizable indexes** — declare the indexes of a collection, with automatic indexes for the id paths
  of referenced documents.
- **Read-only access** — deny writes on a whole db context or on a single repository, to safely consume
  collections owned by another application.
- **Background maintenance tasks** — executed by [Hangfire](https://www.hangfire.io/) by default, or by
  your own task runner.
- **Dependency injection first** — native ASP.NET Core and Hangfire integration, with an optional admin
  dashboard to monitor contexts and run migrations.

## Packages

Five NuGet packages are published, from the full stack down to the single components:

| Package | Use it for | Depends on |
| --- | --- | --- |
| [**MongODM**](https://www.nuget.org/packages/MongODM/) | **Start here.** Meta package wiring the full default stack (ASP.NET Core + Hangfire) behind a single entry point, `AddMongODMWithHangfire`. | `MongODM.AspNetCore`, `MongODM.Hangfire` |
| [**MongODM.Core**](https://www.nuget.org/packages/MongODM.Core/) | The framework itself: mapping, serialization, repositories, db contexts, migrations and tasks. Host-agnostic, always pulled in transitively. Reference it directly for a non-ASP.NET host, or to implement custom components. | — |
| [**MongODM.AspNetCore**](https://www.nuget.org/packages/MongODM.AspNetCore/) | ASP.NET Core integration with a task runner **other than Hangfire**: the `AddMongODM` configuration builder, db context registration, execution context wiring. | `MongODM.Core` |
| [**MongODM.AspNetCore.UI**](https://www.nuget.org/packages/MongODM.AspNetCore.UI/) | The optional admin dashboard, a Razor Pages area monitoring db contexts, their model schemas, their document structures, their missing origin references, their deprecated schema id elements, and running migrations. | `MongODM.AspNetCore` |
| [**MongODM.Hangfire**](https://www.nuget.org/packages/MongODM.Hangfire/) | Scheduling MongODM's maintenance tasks on Hangfire. Pair it with `MongODM.AspNetCore` when you compose the stack yourself. | `MongODM.Core` |

## Installation

```bash
# Standard ASP.NET Core app, with Hangfire running the background tasks:
dotnet add package MongODM

# Optional admin dashboard:
dotnet add package MongODM.AspNetCore.UI
```

`MongODM.AspNetCore`, `MongODM.Hangfire` and `MongODM.Core` come in transitively; add one of them
directly only when you compose the stack yourself.

## Quick start

A small register of cats, from the domain model to the first query. The complete runnable version is
[`samples/AspNetCoreSample`](samples/AspNetCoreSample); a local MongoDB instance is all you need.

### 1. Model your domain

Entities have an identity and implement `IEntityModel<TKey>`; value objects have none and implement
`IModel`. Defining two abstract bases and deriving the domain from them keeps things tidy:

```csharp
using Etherna.Scrinium.Core.Domain.Models;

public abstract class ModelBase : IModel
{
    public virtual IDictionary<string, object>? ExtraElements { get; protected set; }
}

public abstract class EntityModelBase<TKey> : ModelBase, IEntityModel<TKey>
{
    public virtual TKey Id { get; protected set; } = default!;

    public virtual void DisposeForDelete() { }
}

public class Cat : EntityModelBase<string>
{
    public Cat(string name, DateTime birthday)
    {
        Birthday = birthday;
        Name = name;
    }
    protected Cat() { }

    public virtual DateTime Birthday { get; protected set; }
    public virtual string Name { get; protected set; } = null!;

    public virtual void Rename(string name) => Name = name;
}
```

Every member is `virtual`: MongODM subclasses persisted models with a **proxy generated at compile
time** (a source generator shipped inside `MongODM.Core`) to provide lazy loading and change tracking.
The protected parameterless constructor lets the serializer materialize an instance, and `Id` is left
unset — the id generator configured by the map assigns it on insert.

### 2. Map the models

A **model map** tells the serializer how a model is written to and read from documents. The first
argument is the map's **schema id**, an immutable unique string stamped into every document it writes:
this is what lets several schema versions coexist in the same collection.

```csharp
using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Bson.Serialization.IdGenerators;
using Etherna.MongoDB.Bson.Serialization.Serializers;
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Serialization;

class SampleModelMaps : IModelMapsCollector
{
    public void Register(IDbContextEngine dbContextEngine)
    {
        dbContextEngine.MapRegistry.AddModelMap<ModelBase>("1252861f-82d9-4c72-975e-3571d5e1b6e6");

        dbContextEngine.MapRegistry.AddModelMap<EntityModelBase<string>>(
            "81dd8b35-a0af-44d9-80b4-ab7ae9844eb5",
            schema =>
            {
                schema.AutoMap();

                // Persist the string id as an ObjectId, generated on insert.
                schema.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId))
                                  .SetIdGenerator(new StringObjectIdGenerator());
            });

        dbContextEngine.MapRegistry.AddModelMap<Cat>("cd37bafa-a36d-4b1f-815a-deb50c49d030");
    }
}
```

Without the optional configuration argument, MongODM applies `AutoMap()`.

### 3. Declare a db context

A db context is the **unit of work** exposing the repositories of one domain boundary. Declaring an
interface for it is recommended, for dependency injection and testing:

```csharp
using Etherna.Scrinium.Core;
using Etherna.Scrinium.Core.Repositories;
using Etherna.Scrinium.Core.Serialization;

public interface ISampleDbContext : IDbContext
{
    IRepository<Cat, string> Cats { get; }
}

public class SampleDbContext : DbContext, ISampleDbContext
{
    public IRepository<Cat, string> Cats { get; } = new Repository<Cat, string>("cats");

    protected override IEnumerable<IModelMapsCollector> ModelMapsCollectors =>
        [new SampleModelMaps()];

    // Runs once, to populate an empty database.
    protected override Task SeedAsync() => base.SeedAsync();
}
```

### 4. Register it

```csharp
using Etherna.Scrinium.AspNetCore.Extensions;
using Etherna.Scrinium.AspNetCore.UI;   // only with the admin dashboard
using Etherna.Scrinium.Extensions;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHangfireServer();

builder.Services.AddMongODMWithHangfire(hangfireOptions =>
    {
        hangfireOptions.ConnectionString = builder.Configuration.GetConnectionString("HangfireDb")!;
    })
    .AddDbContext<ISampleDbContext, SampleDbContext>(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("SampleDb")!;
    });

builder.Services.AddMongODMAdminDashboard();   // optional

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();      // required by the dashboard
app.MapRazorPages();

app.SeedDbContexts();        // runs each db context's SeedAsync

app.Run();
```

Each db context is registered as a **scoped** instance attached to a **singleton** engine: process-wide
state (registries, connections) lives on the engine, while the unit of work lives on the scope.

The connection string of a db context declares the database name as its path segment
(`mongodb://localhost:27017/mydb`): one without it is rejected at startup.

The dashboard authorizes requests through `DashboardOptions.AuthFilters`, and grants access when every
one of them allows: an empty filter list leaves it unrestricted, for an application with no
authorization of its own. The default filter only
accepts requests coming directly from the host running the application, and denies any request
carrying forwarding headers: behind a reverse proxy the connection address identifies the proxy, not
the client, so replace the default with a filter validating an authenticated principal.

### 5. Use the repositories

Inject the db context interface and work with its repositories:

```csharp
using Etherna.MongoDB.Driver.Linq;   // ToListAsync on the LINQ query

public class CatsService(ISampleDbContext dbContext)
{
    public async Task<IEnumerable<Cat>> RenameKittyAsync()
    {
        // Insert a document, its id is generated.
        await dbContext.Cats.CreateAsync(
            new Cat("Kitty", new DateTime(2021, 3, 14, 0, 0, 0, DateTimeKind.Utc)));

        // Query the collection with LINQ.
        var cats = await dbContext.Cats.QueryElementsAsync(elements =>
            elements.Where(cat => cat.Name == "Kitty")
                    .ToListAsync());

        // Mutate and save: only the changed members are written, atomically.
        cats.First().Rename("Kitten");
        await dbContext.SaveChangesAsync();

        return cats;
    }
}
```

### Reference another entity, keep it in sync

This is MongODM's flagship feature: a referenced entity is stored once in its own collection, and every
referencing document embeds a **summary** of it — its id plus the members you chose to denormalize.
Give each cat an owner — a `Person Owner` member on `Cat` — and serialize it as a reference:

```csharp
using Etherna.Scrinium.Core.Serialization.Serializers;

// The Person summary embedded into every document referencing one: id, plus the name.
public static ReferenceSerializer<Person, string> PersonReference(IDbContextEngine engine) =>
    new(engine, config =>
    {
        config.AddModelMap<ModelBase>("d1a4e1b0-…", map => { });   // no summary members at this level
        config.AddModelMap<EntityModelBase<string>>("6e7f0c22-…", map =>
        {
            map.MapIdMember(m => m.Id);   // every summary must carry the id
            map.IdMemberMap.SetSerializer(new StringSerializer(BsonType.ObjectId));
        });
        config.AddModelMap<Person>("9b53a7d4-…", map => map.MapMember(person => person.Name));
    });

// Apply it to the referencing member, in the Cat map of step 2.
dbContextEngine.MapRegistry.AddModelMap<Cat>("cd37bafa-a36d-4b1f-815a-deb50c49d030", schema =>
{
    schema.AutoMap();
    schema.GetMemberMap(cat => cat.Owner).SetSerializer(PersonReference(dbContextEngine));
});
```

Reading a `Cat` now gives its owner's name with no second query; reading any other member of the owner
lazy-loads the full document from the person repository, or preload it explicitly with
`IDbContext.LoadValuesAsync`. If that person's document is deleted while cats keep referencing them,
the load has nothing to read: by default it logs a warning and gives up the summary state, and the
reference can declare otherwise with `config.MissingOriginDocument` — silent tolerance, or the strict
`Throw` denying the load with `MongodmMissingOriginDocumentException`. Deleting a person **through
their repository** doesn't leave that state behind anyway: by default the reference is removed from
every cat in background — single members set to null, array items pulled — and the reference can
declare otherwise with `config.OriginDelete`: cascade the delete to the referencing documents, or
keep the reference dangling. Rename that person and save: every cat document referencing them is
updated in background. Link a person that was never persisted, and saving the cat creates them first.
The admin dashboard renders the whole structure of the documents of each collection, one per registered
schema, tagging the elements that carry such a summary and expanding the members denormalized into
them: its document structures section tells how a document looks in the database. Its missing origin
references section finds, collection by collection, the references whose origin document doesn't exist
anymore, and can remove them: array items are pulled out of their arrays, single references are set to
null.

## Documentation

The complete documentation lives in the **[MongODM wiki](https://github.com/Etherna/scrinium/wiki)**:
first steps, startup and configuration, domain models and mapping, references and denormalization,
versioned schemas, migrations, transactions, the admin dashboard, and an exceptions reference.

A full runnable app is in [`samples/AspNetCoreSample`](samples/AspNetCoreSample): the register of cats
above, with their owners referenced, a read-only db context, a secondary schema and a document
migration to run from the dashboard.

Architecture diagrams under [`doc/`](doc) are maintained with [diagrams.net](https://www.diagrams.net/).

## Supported frameworks

The libraries multi-target **.NET 8, 9 and 10**. Install them into any project on a compatible
framework.

The MongoDB driver they use is the **Etherna fork** (`Etherna.MongoDB.Driver`, namespaces
`Etherna.MongoDB.*`), brought in with the packages: reference MongODM's packages, not the official
`MongoDB.*` ones.

## Building and testing

MongODM builds with the standard .NET SDK:

```bash
dotnet restore Scrinium.sln
dotnet build   Scrinium.sln -c Release   # compiles every target framework
dotnet test    Scrinium.sln -c Release   # runs the xUnit test projects

dotnet run --project samples/AspNetCoreSample   # the sample app, needs a local MongoDB
```

`TreatWarningsAsErrors=true` and `AnalysisMode=AllEnabledByDefault` are enabled across the solution, so
warnings break the build on every target framework. Because the libraries compile against the lowest
target (`net8.0`), avoid APIs introduced only in a later framework — code can pass locally on `net10.0`
yet fail the `net8.0` build. A green `dotnet build Scrinium.sln` means all target frameworks compiled.

The integration tests need a real MongoDB instance supporting transactions: they use the
`MONGODM_TEST_DB_URL` environment variable when set, otherwise they spawn a throwaway local `mongod`
process as a single node replica set (the binary must be on `PATH`).

Versions are computed by [GitVersion](https://gitversion.net/), there are no manual version bumps.

Coding conventions and architecture notes live in [AGENTS.md](AGENTS.md).

## Project layout

```
src/
  Scrinium                  meta package wiring the full stack (→ AspNetCore, Hangfire)
  Scrinium.AspNetCore       dependency injection integration for ASP.NET Core (→ Core)
  Scrinium.AspNetCore.UI    admin dashboard, as a Razor Pages area (→ AspNetCore)
  Scrinium.Core             the framework: mapping, repositories, db contexts, migrations, tasks
  Scrinium.Core.Generators  source generator emitting the model proxies (packed inside Core)
  Scrinium.Hangfire         task runner scheduling MongODM's tasks on Hangfire (→ Core)
test/
  Scrinium.AspNetCore.UI.UnitTests    admin dashboard tests, over an in-memory test host
  Scrinium.Core.UnitTests             xUnit + Moq unit tests of the core
  Scrinium.Core.Generators.UnitTests  generator tests, running it in-memory on sample compilations
  Scrinium.IntegrationTests           end-to-end tests against a real MongoDB instance
samples/
  AspNetCoreSample          runnable demo web app (not packed)
```

## Package repositories

You can get the latest public releases from the [NuGet.org feed](https://www.nuget.org/profiles/etherna).

If you'd like to work with the latest internal releases, you can use our
[custom MyGet feed](https://www.myget.org/F/etherna/api/v3/index.json) (NuGet V3).

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and our
[Code of Conduct](CODE_OF_CONDUCT.md) before opening a pull request.

## Issue reports

If you've discovered a bug, or have an idea for a new feature, please report it to our issue manager
based on Jira: https://etherna.atlassian.net/projects/MODM.

Detailed reports with stack traces, actual and expected behaviours are welcome.

## Questions? Problems?

For questions or problems please write an email to [info@etherna.io](mailto:info@etherna.io).

## License

![LGPL Logo](https://www.gnu.org/graphics/lgplv3-with-text-154x68.png)

We use the GNU Lesser General Public License v3 or later (SPDX `LGPL-3.0-or-later`) for this project:
[COPYING.LESSER](COPYING.LESSER) adds the lesser terms to the GNU GPL v3 of [COPYING](COPYING).
If you require a custom license, you can contact us at [license@etherna.io](mailto:license@etherna.io).
