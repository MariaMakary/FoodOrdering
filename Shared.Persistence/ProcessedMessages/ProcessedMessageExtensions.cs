using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging;

namespace Shared.Persistence.ProcessedMessages;

public static class ProcessedMessageExtensions
{
    public static IServiceCollection AddProcessedMessages<TContext, TProcessedMessage>(this IServiceCollection services)
        where TContext : DbContext
        where TProcessedMessage : class, IProcessedMessage, new()
    {
        services.AddScoped<IProcessedMessageStore, ProcessedMessageStore<TContext, TProcessedMessage>>();
        return services;
    }
}
