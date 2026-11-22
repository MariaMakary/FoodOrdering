using Admin.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Admin.Web.Repositories;

public class ExampleRepository(AdminDbContext context) : IExampleRepository
{
    public Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default) =>
        context.MenuItems.CountAsync(m => !m.IsDeleted, cancellationToken);
}
