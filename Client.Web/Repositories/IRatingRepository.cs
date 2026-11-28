using Client.Web.Models.Entities;
using Client.Web.Models.Identity;

namespace Client.Web.Repositories;

public interface IRatingRepository
{
    Task<Order?> GetOrderForRatingAsync(Guid orderId, CancellationToken ct = default);

    Task<ApplicationUser?> GetCustomerAsync(Guid customerId, CancellationToken ct = default);

    void Add(Rating rating);

    Task<bool> TrySaveChangesAsync(CancellationToken ct = default);
}
