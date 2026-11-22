using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Messages;

namespace Shared.Persistence.Outbox;

public class EfOutbox<TContext>(TContext context) : IOutbox where TContext : DbContext
{
    public void Add(MessageBase message) => context.Set<OutboxMessage>().Add(OutboxMessage.From(message));
}
