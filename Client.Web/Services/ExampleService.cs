using Client.Web.Repositories;

namespace Client.Web.Services;

public class ExampleService(IExampleRepository repository) : IExampleService
{
    public Task<int> CountMenuItemsAsync(CancellationToken cancellationToken = default) =>
        repository.CountMenuItemsAsync(cancellationToken);
}
