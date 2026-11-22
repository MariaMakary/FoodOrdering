using Shared.Contracts.Messages;

namespace Shared.Persistence.Outbox;

public interface IOutbox
{
    void Add(MessageBase message);
}
