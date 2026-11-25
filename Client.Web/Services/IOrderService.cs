using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public interface IOrderService
{
    Task<ServiceResult<CheckoutViewModel>> GetCheckoutAsync(Guid customerId, CancellationToken ct = default);

    Task<ServiceResult<Guid>> PlaceOrderAsync(Guid customerId, CheckoutViewModel model, CancellationToken ct = default);

    Task<IReadOnlyList<OrderListItemViewModel>> GetCustomerOrdersAsync(Guid customerId, CancellationToken ct = default);

    Task<OrderDetailsViewModel?> GetOrderDetailsAsync(Guid orderId, Guid customerId, CancellationToken ct = default);

    Task<ServiceResult> CancelOrderAsync(Guid orderId, Guid customerId, CancellationToken ct = default);
}
