using Client.Web.Models.ViewModels;

namespace Client.Web.Services;

public interface IRatingService
{
    Task<ServiceResult<RateOrderViewModel>> GetRateFormAsync(Guid orderId, Guid customerId, CancellationToken ct = default);

    Task<ServiceResult> SubmitAsync(Guid customerId, RateOrderViewModel model, CancellationToken ct = default);
}
