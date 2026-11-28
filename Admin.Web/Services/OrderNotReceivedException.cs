namespace Admin.Web.Services;

public class OrderNotReceivedException(Guid orderId, string messageType)
    : Exception($"Order {orderId} does not exist yet, so the {messageType} message can't be applied. It is retried until the OrderPlaced message arrives.")
{
    public Guid OrderId { get; } = orderId;

    public string MessageType { get; } = messageType;
}
