# MongODM

## Overview

MongODM is a **ODM framework** (Object-Documental Mapper) developed for work with **MongoDB** and **Asp.NET Core** applications.

With MongODM you can concentrate on developing your application, without worrying on how data is organized in documents. **Documental databases are very efficient**, but often using them we have also to concern about **how data is structured** at the application layer. MongODM creates a document-to-object mapping layer, solving this kind of problems.

The real powerful and different vision of documental DBs against SQL DBs resides into **data denormalization**. With denormalization we can optimize data structure for get all required information with a single document read, without join, but price of this efficiency is often paid with a more difficult to develop application layer. Indeed, often writing applications with documental DBs we can encounter these kind of issues:

- Related models are composed with application domain, but different models are saved into different documents. Logical join operations are so performed on application layer, instead of db layer, using different queries on db
- Denormalization can optimize these reading operations, but when application requirements evolve, we can have to update the schema of all saved documents. Or even worse, we could have to decorate our application with explicit checks and conditions on loaded data
- Update a document is easy, but update a document that has been denormalized into others can be really painful. We have to trace where denormalization has gone, and update every dependent document

For these reasons SQL databases are often still prefered to documentals, because data management with complex domains is easier, even if documental can be more efficient. Instead often documental DBs are used as a simple CRUD storage layer, for save only plain data without make any use of denormalization advantages.

**MongODM has the scope to solve these problems**, and its scope is to **bring efficiency of denormalized documental data also to complex application domains**, without have to worry at all of how data is organized from an application layer!

Here is a non exhaustive list of implemented features by MongODM:

- Create relation between documents, and fine configuration of serialized properties with document denormalization
- Transparent lazy loading of unloaded properties on related documents
- Manage repositories with database contexts
- Execute ACID transactions with automatic enlistment of repository operations
- Automatic denormalized documents update
- Execute database maintenance tasks with asynchronous task manager [Hangfire](https://www.hangfire.io/) by default, or with your custom one
- Customizable database indexes
- Handle different versioned document schemas with same database collection
- Configurable data migration scripts between document schemas
- Oriented to Dependency Injection for improve code testability
- Native integration with Asp.NET Core and Hangfire

**Disclaimer**: MongODM is still in a pre-beta phase, new features are going to be implemented, and current interface is still susceptible to heavy changes. At this stage active use in production is discouraged.

## Package repositories

You can get latest public releases from Nuget.org feed. Here you can see our [published packages](https://www.nuget.org/profiles/etherna).

If you'd like to work with latest internal releases, you can use our [custom feed](https://www.myget.org/F/etherna/api/v3/index.json) (NuGet V3) based on MyGet.

## Documentation

For specific documentation on how to install and use MongODM, visit our [Wiki](https://github.com/Etherna/mongodm/wiki).

Graphic documentation is maintained with https://www.diagrams.net/ software.

## Issue reports

If you've discovered a bug, or have an idea for a new feature, please report it to our issue manager based on Jira https://etherna.atlassian.net/projects/MODM.

Detailed reports with stack traces, actual and expected behaviours are welcome.

## Questions? Problems?

For questions or problems please write an email to [info@etherna.io](mailto:info@etherna.io).

## License

![LGPL Logo](https://www.gnu.org/graphics/lgplv3-with-text-154x68.png)

We use the GNU Lesser General Public License v3 (LGPL-3.0) for this project.
If you require a custom license, you can contact us at [license@etherna.io](mailto:license@etherna.io).

