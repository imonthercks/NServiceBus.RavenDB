﻿namespace NServiceBus.AcceptanceTests.NonDTC
{
    using AcceptanceTesting.Customization;
    using NServiceBus.AcceptanceTesting;
    using NServiceBus.AcceptanceTests.EndpointTemplates;
    using NServiceBus.Configuration.AdvanceExtensibility;
    using NServiceBus.Features;
    using NUnit.Framework;
    using System;
    using System.Threading.Tasks;

    public class When_outbox_is_enabled : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Downstream_duplicates_are_eliminated()
        {
            var context = await Scenario.Define<Context>()
                .WithEndpoint<EndpointWithOutboxAndAuditOn>(b =>
                {
                    b.When(async busSession =>
                    {
                        var sendOptions = new SendOptions();
                        sendOptions.SetMessageId("KickoffMessage-" + Guid.NewGuid().ToString());
                        sendOptions.RouteToThisEndpoint();

                        await busSession.Send(new KickoffMessage(), sendOptions);
                    })
                    .DoNotFailOnErrorMessages();
                })
                .WithEndpoint<DownstreamEndpoint>()
                .Done(c => c.Done)
                .Run();

            Assert.IsTrue(context.Done);
            Assert.AreEqual(1, context.DownstreamMessageCount);
        }

        public class Context : ScenarioContext
        {
            public int DownstreamMessageCount { get; set; }
            public bool Done { get; set; }
        }

        public class KickoffMessage : IMessage
        {
        }

        public class DuplicateMessage : IMessage
        {
        }

        public class MarkerMessage : IMessage
        {
        }

        public class DownstreamMessage : IMessage
        {
        }

        public class EndpointWithOutboxAndAuditOn : EndpointConfigurationBuilder
        {
            public EndpointWithOutboxAndAuditOn()
            {
                EndpointSetup<DefaultServer>(
                    b =>
                    {
                        b.GetSettings().Set("DisableOutboxTransportCheck", true);
                        b.EnableFeature<TimeoutManager>();
                        b.EnableOutbox();

                        var routingConfig = b.ConfigureTransport().Routing();
                        routingConfig.RouteToEndpoint(typeof(DownstreamMessage), typeof(DownstreamEndpoint));
                        routingConfig.RouteToEndpoint(typeof(MarkerMessage), typeof(DownstreamEndpoint));
                    });
            }

            class KickoffMessageHandler : IHandleMessages<KickoffMessage>
            {
                public async Task Handle(KickoffMessage message, IMessageHandlerContext context)
                {
                    var sendOptions = new SendOptions();
                    sendOptions.SetMessageId("DuplicateMessage-" + Guid.NewGuid().ToString());
                    sendOptions.RouteToThisEndpoint();

                    await context.Send(new DuplicateMessage(), sendOptions);
                    await context.Send(new DuplicateMessage(), sendOptions);

                    await context.SendLocal(new MarkerMessage());
                }
            }

            class DuplicateMessageHandler : IHandleMessages<DuplicateMessage>
            {
                public Task Handle(DuplicateMessage message, IMessageHandlerContext context)
                {
                    return context.Send(new DownstreamMessage());
                }
            }

            class MarkerMessageHandler : IHandleMessages<MarkerMessage>
            {
                public Task Handle(MarkerMessage message, IMessageHandlerContext context)
                {
                    return context.Send(message);
                }
            }
        }

        public class DownstreamEndpoint : EndpointConfigurationBuilder
        {
            public DownstreamEndpoint()
            {
                EndpointSetup<DefaultServer>();
            }

            class DownstreamMessageHandler : IHandleMessages<DownstreamMessage>
            {
                public Context Context { get; set; }

                public Task Handle(DownstreamMessage message, IMessageHandlerContext context)
                {
                    Context.DownstreamMessageCount++;
                    return Task.FromResult(0);
                }
            }

            class MarkerMessageHandler : IHandleMessages<MarkerMessage>
            {
                public Context Context { get; set; }

                public Task Handle(MarkerMessage message, IMessageHandlerContext context)
                {
                    Context.Done = true;
                    return Task.FromResult(0);
                }
            }
        }
    }
}