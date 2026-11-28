using Admin.Web;
using Admin.Web.Data;
using Admin.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Contracts.Messages;
using Shared.Messaging;
using Shared.Persistence.Outbox;
using Shared.Persistence.ProcessedMessages;

namespace FoodOrdering.IntegrationTests.Admin;

public static class AdminConsumerHost
{
    public static IHost Build(string connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Username"] = "unused",
            ["RabbitMq:Password"] = "unused",
            ["RabbitMq:ExchangeName"] = "food.events"
        });
        builder.Services.AddRabbitMq(builder.Configuration);
        builder.Services.AddDbContext<AdminDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddOutbox<AdminDbContext>();
        builder.Services.AddProcessedMessages<AdminDbContext, ProcessedMessage>();
        builder.Services.AddAdminServices();
        return builder.Build();
    }

    public static TConsumer ConsumerOf<TConsumer>(IHost host) where TConsumer : IHostedService =>
        host.Services.GetServices<IHostedService>().OfType<TConsumer>().Single();

    public static async Task HandleAsync<TConsumer, TMessage>(string connectionString, TMessage message)
        where TConsumer : RabbitMqConsumerBase<TMessage>
        where TMessage : MessageBase
    {
        using IHost host = Build(connectionString);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await ConsumerOf<TConsumer>(host).HandleAsync(message, scope.ServiceProvider, CancellationToken.None);
    }
}
