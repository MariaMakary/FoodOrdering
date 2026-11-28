using Admin.Web.Services;
using Shared.Contracts.Messages;
using Shared.Messaging;

namespace Admin.Web.Messaging.Consumers;

public class PaymentCompletedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentCompletedConsumer> logger) : RabbitMqConsumerBase<PaymentCompleted>(connection, scopeFactory, logger)
{
    public override Task HandleAsync(PaymentCompleted message, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<IPaymentService>().RecordPaymentAsync(message, ct);
}
