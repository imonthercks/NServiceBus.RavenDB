﻿using System.Linq;
using System.Threading.Tasks;
using NServiceBus.Extensibility;
using NServiceBus.RavenDB.Persistence.SubscriptionStorage;
using NServiceBus.RavenDB.Tests;
using NServiceBus.Unicast.Subscriptions;
using NServiceBus.Unicast.Subscriptions.RavenDB;
using NUnit.Framework;

[TestFixture]
public class When_receiving_a_subscription_message : RavenDBPersistenceTestBase
{
    [Test]
    public async Task A_subscription_entry_should_be_added_to_the_database()
    {
        var clientEndpoint = "TestEndpoint";

        var messageTypes = new[]
        {
            new MessageType("MessageType1", "1.0.0.0"),
            new MessageType("MessageType2", "1.0.0.0")
        };

        var storage = new SubscriptionPersister(store);

        await storage.Subscribe(clientEndpoint, messageTypes, new ContextBag());

        using (var session = store.OpenSession())
        {
            var subscriptions = session
                .Query<Subscription>()
                .Customize(c => c.WaitForNonStaleResults())
                .Count();

            Assert.AreEqual(2, subscriptions);
        }
    }
}