using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Shared.Messaging;

public sealed class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    IHostEnvironment environment,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync(ct);
        try
        {
            _connection ??= await ConnectAsync(ct);
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken ct = default)
    {
        IConnection connection = await GetAsync(ct);
        return await connection.CreateChannelAsync(cancellationToken: ct);
    }

    private async Task<IConnection> ConnectAsync(CancellationToken ct)
    {
        RabbitMqOptions settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            UserName = settings.Username,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost
        };

        int attempt = 0;
        while (true)
        {
            try
            {
                IConnection connection = await factory.CreateConnectionAsync(environment.ApplicationName, ct);
                logger.LogInformation("Connected to RabbitMQ at {Host}:{Port} as {ConnectionName}",
                    settings.Host, settings.Port, environment.ApplicationName);
                return connection;
            }
            catch (BrokerUnreachableException exception) when (attempt < settings.RetryCount)
            {
                attempt++;
                TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(exception, "RabbitMQ is not reachable, retrying in {Delay} (attempt {Attempt} of {RetryCount})",
                    delay, attempt, settings.RetryCount);
                await Task.Delay(delay, ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
