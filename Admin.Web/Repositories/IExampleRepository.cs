namespace Admin.Web.Repositories;

public interface IExampleRepository
{
    Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default);
}
