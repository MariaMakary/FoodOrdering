using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public interface IOrderService
{
    Task<ServiceResult<CheckoutViewModel>> GetCheckoutAsync(Guid customerId, CancellationToken ct = default);

    Task<ServiceResult<Guid>> PlaceOrderAsync(Guid customerId, CheckoutViewModel model, CancellationToken ct = default);
}
