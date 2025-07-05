using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace UTimer;

public static class UTimerExtensions
{
    public static IServiceCollection AddUTimer(this IServiceCollection services, int maxDegreeOfParallelism = 20, int maxQueueSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(services);

        var channel = Channel.CreateBounded<Func<IServiceProvider, Task>>(new BoundedChannelOptions(maxQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        services.AddSingleton(channel);
        services.AddHostedService(sp =>
            new JobBackgroundService(
                sp,
                sp.GetRequiredService<ILogger<JobBackgroundService>>(),
                channel,
                maxDegreeOfParallelism));

        return services;
    }
}

public static class JobCreator
{
    private static Channel<Func<IServiceProvider, Task>>? _channel;

    internal static void Initialize(Channel<Func<IServiceProvider, Task>> channel)
    {
        _channel = channel;
    }

    public static async Task Enqueue<TService>(Func<TService, Task> job) where TService : class
    {
        ArgumentNullException.ThrowIfNull(job);

        Task Work(IServiceProvider sp)
        {
            var service = sp.GetRequiredService<TService>();
            return job(service);
        }

        if (_channel == null)
            throw new InvalidOperationException("JobCreator is not initialized. Call AddUTimer first.");

        await _channel.Writer.WriteAsync(Work);
    }
}

public class JobBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<JobBackgroundService> logger,
    Channel<Func<IServiceProvider, Task>> channel,
    int maxDegreeOfParallelism = 20)
    : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism, maxDegreeOfParallelism);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobCreator.Initialize(channel); // Enqueue'de kanal kullanılabilsin

        var reader = channel.Reader;
        var runningTasks = new List<Task>();

        await foreach (var job in reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);

            var task = Task.Run(async () =>
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    await job(scope.ServiceProvider);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background job failed");
                }
                finally
                {
                    _semaphore.Release();
                }
            }, stoppingToken);

            runningTasks.Add(task);

            // Completed işleri temizle
            runningTasks.RemoveAll(t => t.IsCompleted);
        }

        // Kuyruk kapanınca kalan işleri bekle
        await Task.WhenAll(runningTasks);
    }
}
