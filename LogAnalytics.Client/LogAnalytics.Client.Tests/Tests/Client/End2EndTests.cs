﻿using LogAnalytics.Client.Tests.Helpers;
using LogAnalytics.Client.Tests.TestEntities;
using Microsoft.Azure.OperationalInsights;
using Microsoft.Rest.Azure.Authentication;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LogAnalytics.Client.Tests.Tests
{
    [TestClass]
    public class End2EndTests : TestsBase
    {
        private static OperationalInsightsDataClient _dataClient;
        private static TestSecrets _secrets;
        private static string testIdentifierEntries;
        private static string testIdentifierEntry;

        // Init: push some data into the LAW, then wait for a bit, then we'll run all the e2e tests.
        [ClassInitialize]
        public static void Init(TestContext context)
        {
            // Wire up test secrets.
            _secrets = InitSecrets();

            // Get a data client, helping us actually Read data, too.
            _dataClient = GetLawDataClient(_secrets.LawSecrets.LawId, _secrets.LawPrincipalCredentials.ClientId, _secrets.LawPrincipalCredentials.ClientSecret, _secrets.LawPrincipalCredentials.Domain).Result;

            // Set up unique identifiers for the tests. This helps us query the Log Analytics Workspace for our specific messages, and ensure the count and properties are correctly shipped to the logs.
            testIdentifierEntries = $"test-id-{Guid.NewGuid()}";
            testIdentifierEntry = $"test-id-{Guid.NewGuid()}";

            // Initialize the LAW Client.
            LogAnalyticsClient logger = new LogAnalyticsClient(
                workspaceId: _secrets.LawSecrets.LawId,
                sharedKey: _secrets.LawSecrets.LawKey);

            // Test 1 prep: Push a collection of entities to the logs.
            List<DemoEntity> entities = new List<DemoEntity>();
            for (int ii = 0; ii < 12; ii++)
            {
                entities.Add(new DemoEntity
                {
                    Criticality = "e2ecriticality",
                    Message = testIdentifierEntries,
                    SystemSource = "e2etest"
                });
            }

            logger.SendLogEntries(entities, "endtoendlogs").Wait();


            // Test 2 prep: Send a single entry to the logs.
            logger.SendLogEntry(new DemoEntity
            {
                Criticality = "e2ecriticalitysingleentry",
                Message = testIdentifierEntry,
                SystemSource = "e2etestsingleentry"
            }, "endtoendlogs").Wait();

            // Since it takes a while before the logs are queryable, we'll sit tight and wait for a few minutes before we launch the retrieval-tests.
            Thread.Sleep(6 * 1000 * 60);
        }

        [TestMethod]
        public void E2E_VerifySendLogEntries_Test()
        {
            var query = _dataClient.Query($"endtoendlogs_CL | where Message == '{testIdentifierEntries}' | order by TimeGenerated desc | limit 20");
            Assert.AreEqual(12, query.Results.Count());
            Assert.AreEqual(testIdentifierEntries, query.Results.First()["Message"]);

            var entry = query.Results.First();
            Assert.AreEqual(testIdentifierEntries, entry["Message"]);
            Assert.AreEqual("e2etest", entry["SystemSource_s"]);
            Assert.AreEqual("e2ecriticality", entry["Criticality_s"]);
        }

        [TestMethod]
        public void E2E_VerifySendLogEntry_Test()
        {
            var query = _dataClient.Query($"endtoendlogs_CL | where Message == '{testIdentifierEntry}' | order by TimeGenerated desc | limit 10");
            Assert.AreEqual(1, query.Results.Count());

            var entry = query.Results.First();
            Assert.AreEqual(testIdentifierEntry, entry["Message"]);
            Assert.AreEqual("e2etestsingleentry", entry["SystemSource_s"]);
            Assert.AreEqual("e2ecriticalitysingleentry", entry["Criticality_s"]);
        }

        // TODO: Enhance test coverage in the E2E tests
        // - Cover custom types and entities
        // - Cover huge amounts of data
        // - Cover special charachers

        private static async Task<OperationalInsightsDataClient> GetLawDataClient(string workspaceId, string lawPrincipalClientId, string lawPrincipalClientSecret, string domain)
        {
            // Note 2020-07-26. This is from the Microsoft.Azure.OperationalInsights nuget, which haven't been updated since 2018. 
            // Possibly we'll look for a REST-approach instead, and create the proper client here.

            var authEndpoint = "https://login.microsoftonline.com";
            var tokenAudience = "https://api.loganalytics.io/";

            var adSettings = new ActiveDirectoryServiceSettings
            {
                AuthenticationEndpoint = new Uri(authEndpoint),
                TokenAudience = new Uri(tokenAudience),
                ValidateAuthority = true
            };

            var credentials = await ApplicationTokenProvider.LoginSilentAsync(domain, lawPrincipalClientId, lawPrincipalClientSecret, adSettings);

            var client = new OperationalInsightsDataClient(credentials)
            {
                WorkspaceId = workspaceId
            };

            return client;
        }

    }
}
