﻿namespace NServiceBus.RavenDB.Outbox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;
    using NServiceBus.Outbox;
    using Raven.Client;
    using TransportOperation = NServiceBus.Outbox.TransportOperation;

    class OutboxPersister : IOutboxStorage
    {
        public string EndpointName { get; set; }

        public OutboxPersister(IDocumentStore documentStore)
        {
            this.documentStore = documentStore;
        }
        public async Task<OutboxMessage> Get(string messageId, ContextBag options)
        {
            OutboxRecord result;
            using (var session = documentStore.OpenAsyncSession())
            {
                session.Advanced.AllowNonAuthoritativeInformation = false;

                // We use Load operation and not queries to avoid stale results
                var docs = await session.LoadAsync<OutboxRecord>(GetPossibleOutboxDocumentIds(messageId)).ConfigureAwait(false);
                result = docs.FirstOrDefault();
            }

            if (result == null)
            {
                return default(OutboxMessage);
            }

            var operations = new List<TransportOperation>();
            if (!result.Dispatched)
            {
                operations.AddRange(result.TransportOperations.Select(t => new TransportOperation(t.MessageId, t.Options, t.Message, t.Headers)));
            }

            var message = new OutboxMessage(result.MessageId, operations);
            return message;
        }


        public Task<OutboxTransaction> BeginTransaction(ContextBag context)
        {
            var session = documentStore.OpenAsyncSession();

            session.Advanced.UseOptimisticConcurrency = true;

            context.Set(session);
            var transaction = new RavenDBOutboxTransaction(session);
            return Task.FromResult<OutboxTransaction>(transaction);
        }

        public async Task Store(OutboxMessage message, OutboxTransaction transaction, ContextBag context)
        {
            var session = ((RavenDBOutboxTransaction)transaction).AsyncSession;

            await session.StoreAsync(new OutboxRecord
            {
                MessageId = message.MessageId,
                Dispatched = false,
                TransportOperations = message.TransportOperations.Select(t => new OutboxRecord.OutboxOperation
                {
                    Message = t.Body,
                    Headers = t.Headers,
                    MessageId = t.MessageId,
                    Options = t.Options
                }).ToList()
            }, GetOutboxRecordId(message.MessageId)).ConfigureAwait(false);
        }

        public async Task SetAsDispatched(string messageId, ContextBag options)
        {
            using (var session = documentStore.OpenAsyncSession())
            {
                session.Advanced.UseOptimisticConcurrency = true;
                session.Advanced.AllowNonAuthoritativeInformation = false;

                var outboxMessage = (await session.LoadAsync<OutboxRecord>(GetPossibleOutboxDocumentIds(messageId)).ConfigureAwait(false)).FirstOrDefault();
                if (outboxMessage == null || outboxMessage.Dispatched)
                {
                    return;
                }

                outboxMessage.Dispatched = true;
                outboxMessage.DispatchedAt = DateTime.UtcNow;

                await session.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        string[] GetPossibleOutboxDocumentIds(string messageId)
        {
            return new[]
            {
                GetOutboxRecordId(messageId),
                $"Outbox/{messageId}"
            };
        }

        string GetOutboxRecordId(string messageId) => $"Outbox/{EndpointName}/{messageId}";


        IDocumentStore documentStore;
    }
}