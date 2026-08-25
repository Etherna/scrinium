// Copyright 2020-present Etherna SA
// This file is part of MongODM.
//
// MongODM is free software: you can redistribute it and/or modify it under the terms of the
// GNU Lesser General Public License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
//
// MongODM is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License along with MongODM.
// If not, see <https://www.gnu.org/licenses/>.

using Etherna.MongoDB.Driver;
using System;
using Xunit;

namespace Etherna.Scrinium.Core.Options
{
    public class DbContextOptionsTest
    {
        // Consts.
        private const string Password = "S3cr3tP%40ss";
        private const string UserInfo = "appuser:" + Password;

        // Tests.
        [Theory]
        [InlineData("mongodb://localhost/localDb", "localDb")]
        [InlineData("mongodb://localhost:27017/mydb", "mydb")]
        [InlineData("mongodb://host1:27017,host2:27017/mydb?replicaSet=rs0", "mydb")]
        [InlineData("mongodb://" + UserInfo + "@db.internal:27017/mydb", "mydb")]
        [InlineData("mongodb://" + UserInfo + "@db.internal:27017/mydb?authSource=admin&replicaSet=rs0", "mydb")]
        [InlineData("mongodb+srv://" + UserInfo + "@cluster.internal/mydb?retryWrites=true", "mydb")]
        public void DbNameIsReadFromConnectionStringPath(string connectionString, string expectedDbName)
        {
            // Setup.
            var options = new DbContextOptions { ConnectionString = connectionString };

            // Action.
            var dbName = options.DbName;

            // Assert.
            Assert.Equal(expectedDbName, dbName);
        }

        [Fact]
        public void DbNameKeepsCredentialsOutOfInvalidConnectionStringErrors()
        {
            /* An unparsable connection string is rejected by the driver, whose message replaces
             * the user info section with a placeholder. */

            // Setup.
            //the unescaped '@' of the password makes the connection string unparsable
            var options = new DbContextOptions { ConnectionString = "mongodb://appuser:S3cr3tP@ss@db.internal:27017/mydb" };

            // Action.
            var exception = Assert.Throws<MongoConfigurationException>(() => options.DbName);

            // Assert.
            Assert.DoesNotContain("appuser", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("S3cr3tP", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("mongodb://db.internal:27017")]
        [InlineData("mongodb://" + UserInfo + "@db.internal:27017")]
        [InlineData("mongodb://" + UserInfo + "@db.internal:27017/")]
        [InlineData("mongodb://" + UserInfo + "@db.internal:27017/?authSource=admin")]
        [InlineData("mongodb+srv://" + UserInfo + "@cluster.internal/?retryWrites=true")]
        public void DbNameThrowsWithoutDatabaseNameInConnectionString(string connectionString)
        {
            // Setup.
            var options = new DbContextOptions { ConnectionString = connectionString };

            // Action.
            var exception = Assert.Throws<InvalidOperationException>(() => options.DbName);

            // Assert.
            Assert.Contains("database name", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(connectionString, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        }
    }
}
