using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shared.Contracts.Options;

namespace FoodOrdering.Tests.Contracts;

public class RabbitMqOptionsTests
{
    [Fact]
    public void Bind_FullSection_SetsAllProperties()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbit.local",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:Username"] = "user",
            ["RabbitMq:Password"] = "pass",
            ["RabbitMq:VirtualHost"] = "food",
            ["RabbitMq:ExchangeName"] = "food.events",
            ["RabbitMq:RetryCount"] = "5"
        });

        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>();

        options.Should().BeEquivalentTo(new RabbitMqOptions
        {
            Host = "rabbit.local",
            Port = 5673,
            Username = "user",
            Password = "pass",
            VirtualHost = "food",
            ExchangeName = "food.events",
            RetryCount = 5
        });
    }

    [Fact]
    public void Bind_PortVirtualHostAndRetryCountMissing_UsesDefaults()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost"
        });

        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()!;

        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.RetryCount.Should().Be(3);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
