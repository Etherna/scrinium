// Copyright 2020-present Etherna SA
// This file is part of Scrinium.
//
// Scrinium is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// Scrinium is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with Scrinium.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Bson;
using Etherna.MongoDB.Driver;
using Etherna.Scrinium.Core.ExecContext.AsyncLocal;
using Etherna.Scrinium.Core.Exceptions;
using Etherna.Scrinium.IntegrationTests.Fixtures;
using Etherna.Scrinium.IntegrationTests.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Etherna.Scrinium.IntegrationTests
{
    [Collection("Integration")]
    public class ReferencedModelTypeChangeTests : IDisposable
    {
        // Fields.
        private readonly ITestDbContext dbContext;
        private readonly IntegrationFixture fixture;
        private readonly IServiceScope serviceScope;

        // Constructor and dispose.
        /* Each test runs on its own DI scope, resolving fresh db context instances
         * like a production request or job would do. */
        public ReferencedModelTypeChangeTests(IntegrationFixture fixture)
        {
            this.fixture = fixture;
            serviceScope = fixture.ServiceProvider.CreateScope();
            dbContext = serviceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
        }

        public void Dispose()
        {
            serviceScope.Dispose();
            GC.SuppressFinalize(this);
        }

        // Tests.
        [Fact]
        public async Task DependenciesUpdateTaskRefreshesSummariesWithTheNewType()
        {
            /* Replacing a referenced model with an instance of another type of its hierarchy,
             * keeping the same id, must propagate the new type to the denormalized summaries:
             * the enqueued dependencies update task reserializes each of them with the
             * reference active schema of the new type, discriminator included. */

            // Setup.
            using var contextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var author = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(author);
            var editor = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(editor);

            var message = new Message("hello", author, editor);
            await dbContext.Messages.CreateAsync(message);

            // Action: evolve the author into a web3 account, keeping its id.
            var web3Author = new Web3Account(author, "0x0123456789");
            await dbContext.Accounts.ReplaceAsync(web3Author);
            await fixture.TaskRunner.ExecutePendingAsync(fixture.ServiceProvider);

            // Assert: the persisted summary is reserialized for the new type.
            var messagesCollection = dbContext.Engine.Database.GetCollection<BsonDocument>("messages");
            var messageFilter = Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(message.Id));
            var rawMessage = await messagesCollection.Find(messageFilter).SingleAsync();

            var rawAuthor = rawMessage["Author"].AsBsonDocument;
            Assert.Equal("Web3Account", ActualTypeDiscriminator(rawAuthor["_t"]));
            Assert.Equal("06d4e4c1-1e57-4bd0-a071-90fe7d3dbc2a", rawAuthor["_s"].AsString); //summary schema of the new type
            Assert.Equal("alice", rawAuthor["Username"].AsString);
            Assert.False(rawAuthor.Contains("EtherAddress")); //summaries keep only their summary members

            //a fresh scope deserializes the reference directly with the new type
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedMessage = await readDbContext.Messages.FindOneAsync(message.Id);
            var loadedAuthor = Assert.IsAssignableFrom<Web3Account>(loadedMessage.Author);
            Assert.Equal("alice", loadedAuthor.Username);
        }

        [Fact]
        public async Task StaleTypedSummaryInvalidatesOnFullLoad()
        {
            /* A summary reference persisted before the type change deserializes with the old
             * type. Its full load finds a document of the new type: the runtime type of an
             * instance can't change, so the stale instance is invalidated, any interaction
             * with it throws a detailed exception, and the repository hands out the fresh
             * instance with the current type. */

            // Setup.
            var (message, _, editorId) = await CreateMessageAndReplaceEditorAsync();

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workDbContext = workScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedMessage = await workDbContext.Messages.FindOneAsync(message.Id);
            var staleEditor = loadedMessage.Editor;
            Assert.IsAssignableFrom<Web2Account>(staleEditor); //the summary still carries the old type

            // Action + assert: the lazy load detects the type change and invalidates the instance.
            var exception = Assert.Throws<ScriniumOutdatedModelTypeException>(() => staleEditor.Username);
            Assert.Contains(nameof(Web2Account), exception.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(Web3Account), exception.Message, StringComparison.Ordinal);
            Assert.Contains(editorId, exception.Message, StringComparison.Ordinal);

            //the model reports as outdated, and every next interaction keeps throwing
            Assert.True(workDbContext.IsOutdatedModel(staleEditor));
            Assert.Throws<ScriniumOutdatedModelTypeException>(() => staleEditor.Username);

            //the id stays readable on the outdated instance, and drives the reload:
            //the repository hands out the fresh instance with the current type
            var freshEditor = await workDbContext.Accounts.FindOneAsync(staleEditor.Id);
            var web3Editor = Assert.IsAssignableFrom<Web3Account>(freshEditor);
            Assert.Equal("bob", web3Editor.Username);
            Assert.Equal("0x9876543210", web3Editor.EtherAddress);
        }

        [Fact]
        public async Task ExplicitPreloadDetectsTheTypeChange()
        {
            /* The explicit preload runs the same full document load of the implicit lazy
             * loading: a preloaded summary of an outdated type invalidates as well, deferring
             * the failure to the first interaction with the stale instance. */

            // Setup.
            var (message, _, _) = await CreateMessageAndReplaceEditorAsync();

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workDbContext = workScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedMessage = await workDbContext.Messages.FindOneAsync(message.Id);
            var staleEditor = loadedMessage.Editor;

            // Action.
            await workDbContext.LoadValuesAsync(staleEditor, m => m.Username);

            // Assert.
            Assert.True(workDbContext.IsOutdatedModel(staleEditor));
            Assert.Throws<ScriniumOutdatedModelTypeException>(() => staleEditor.Username);
        }

        [Fact]
        public async Task ScopeWithOutdatedModelKeepsSavingUnrelatedChanges()
        {
            /* An outdated instance denies any application interaction, but must not poison
             * its scope: saving unrelated changes on a document referencing it keeps working,
             * with the unchanged stale reference member left untouched on the document. */

            // Setup.
            var (message, _, _) = await CreateMessageAndReplaceEditorAsync();

            using var workScope = fixture.ServiceProvider.CreateScope();
            var workDbContext = workScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var workContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedMessage = await workDbContext.Messages.FindOneAsync(message.Id);
            var staleEditor = loadedMessage.Editor;
            Assert.Throws<ScriniumOutdatedModelTypeException>(() => staleEditor.Username);

            // Action: save an unrelated change on the referencing document.
            loadedMessage.Text = "updated text";
            await workDbContext.SaveChangesAsync();

            // Assert.
            using var readScope = fixture.ServiceProvider.CreateScope();
            var readDbContext = readScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var readContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var reloadedMessage = await readDbContext.Messages.FindOneAsync(message.Id);
            Assert.Equal("updated text", reloadedMessage.Text);
        }

        // Helpers.
        /* Create a message whose editor is a web2 account, then replace the editor with its
         * web3 evolution from another scope, skipping the dependencies update task: the
         * message document keeps the stale summary with the old type, like in the transient
         * window before the task execution. */
        private async Task<(Message message, string authorId, string editorId)> CreateMessageAndReplaceEditorAsync()
        {
            using var setupContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();
            fixture.TaskRunner.ClearPending();

            var author = new Web2Account("alice");
            await dbContext.Accounts.CreateAsync(author);
            var editor = new Web2Account("bob");
            await dbContext.Accounts.CreateAsync(editor);

            var message = new Message("hello", author, editor);
            await dbContext.Messages.CreateAsync(message);

            using var replaceScope = fixture.ServiceProvider.CreateScope();
            var replaceDbContext = replaceScope.ServiceProvider.GetRequiredService<ITestDbContext>();
            using var replaceContextHandler = AsyncLocalContext.Instance.InitAsyncLocalContext();

            var loadedEditor = await replaceDbContext.Accounts.FindOneAsync(editor.Id);
            var web3Editor = new Web3Account((Web2Account)loadedEditor, "0x9876543210");
            await replaceDbContext.Accounts.ReplaceAsync(web3Editor);
            fixture.TaskRunner.ClearPending(); //keep the stale summary on the message document

            return (message, author.Id, editor.Id);
        }

        private static string ActualTypeDiscriminator(BsonValue discriminator) =>
            discriminator is BsonArray array ?
                array[array.Count - 1].AsString :
                discriminator.AsString;
    }
}
