using Client.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Client.Web.Repositories;

public class ExampleRepository(ClientDbContext context) : IExampleRepository
{
    public Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default) =>
        context.MenuItems.CountAsync(m => !m.IsDeleted, cancellationToken);
}
