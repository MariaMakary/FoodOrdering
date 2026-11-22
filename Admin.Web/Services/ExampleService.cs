using Admin.Web.Repositories;

namespace Admin.Web.Services;

public class ExampleService(IExampleRepository repository) : IExampleService
{
    public Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default) =>
        repository.CountMenuItemsAsync(cancellationToken);
}
