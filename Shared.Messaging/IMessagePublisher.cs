using Shared.Contracts.Messages;

namespace Shared.Messaging;

public interface IMessagePublisher
{
    Task PublishAsync(MessageBase message, CancellationToken ct = default);
}
