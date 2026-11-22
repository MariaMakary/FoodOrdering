namespace Client.Web.Services;

public interface IExampleService
{
    Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default);
}
