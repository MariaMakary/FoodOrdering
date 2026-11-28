using Admin.Web.Models.Entities;
using Admin.Web.Repositories;
using Shared.Contracts.Enums;
using Shared.Contracts.Messages;

namespace Admin.Web.Services;

public class PaymentService(IPaymentRepository payments, ILogger<PaymentService> logger) : IPaymentService
{
    public async Task RecordPaymentAsync(PaymentCompleted message, CancellationToken ct = default)
    {
        Order order = await payments.GetOrderWithPaymentAsync(message.OrderId, ct)
            ?? throw new OrderNotReceivedException(message.OrderId, nameof(PaymentCompleted));

        if (order.Payment is not null)
        {
            if (order.Payment.Id == message.PaymentId)
            {
                logger.LogInformation("Payment {PaymentId} for order {OrderId} is already saved, ignoring the PaymentCompleted message",
                    message.PaymentId, message.OrderId);
            }
            else
            {
                logger.LogWarning(
                    "Order {OrderId} already has payment {ExistingPaymentId}, ignoring the PaymentCompleted message for payment {PaymentId}",
                    message.OrderId, order.Payment.Id, message.PaymentId);
            }

            return;
        }

        if (message.Amount != order.Total)
        {
            logger.LogWarning("Payment {PaymentId} for order {OrderId} is {Amount} but the order total is {Total}",
                message.PaymentId, message.OrderId, message.Amount, order.Total);
        }

        payments.Add(new Payment
        {
            Id = message.PaymentId,
            OrderId = order.Id,
            Amount = message.Amount,
            Method = message.Method,
            PaidAt = message.PaidAt
        });
        order.PaymentStatus = PaymentStatus.Paid;

        await payments.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderId} marked Paid with payment {PaymentId} of {Amount}",
            order.Id, message.PaymentId, message.Amount);
    }
}
