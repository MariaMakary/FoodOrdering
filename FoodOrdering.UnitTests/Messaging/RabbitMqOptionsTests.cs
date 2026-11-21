using Microsoft.Extensions.Configuration;
using Shared.Messaging;
using Shouldly;

namespace FoodOrdering.UnitTests.Messaging;

public class RabbitMqOptionsTests
{
    [Fact]
    public void Bind_ShouldSetEveryProperty_WhenTheSectionIsComplete()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbit.local",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:Username"] = "user",
            ["RabbitMq:Password"] = "pass",
            ["RabbitMq:VirtualHost"] = "food",
            ["RabbitMq:ExchangeName"] = "food.events",
            ["RabbitMq:RetryCount"] = "5"
        });

        RabbitMqOptions options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()!;

        options.Host.ShouldBe("rabbit.local");
        options.Port.ShouldBe(5673);
        options.Username.ShouldBe("user");
        options.Password.ShouldBe("pass");
        options.VirtualHost.ShouldBe("food");
        options.ExchangeName.ShouldBe("food.events");
        options.RetryCount.ShouldBe(5);
    }

    [Fact]
    public void Bind_ShouldUseDefaults_WhenPortVirtualHostAndRetryCountAreMissing()
    {
        IConfiguration configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "localhost"
        });

        RabbitMqOptions options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()!;

        options.Port.ShouldBe(5672);
        options.VirtualHost.ShouldBe("/");
        options.RetryCount.ShouldBe(3);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
