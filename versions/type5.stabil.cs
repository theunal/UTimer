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
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        services.AddSingleton(channel);
        services.AddSingleton<JobCreator>(); // <- artık singleton servis
        services.AddHostedService(sp =>
            new JobBackgroundService(
                sp,
                sp.GetRequiredService<ILogger<JobBackgroundService>>(),
                sp.GetRequiredService<JobCreator>(),
                maxDegreeOfParallelism));

        return services;
    }
}

public class JobCreator(Channel<Func<IServiceProvider, Task>> channel)
{
    public async Task EnqueueAsync<TService>(Func<TService, Task> job) where TService : class
    {
        ArgumentNullException.ThrowIfNull(job);

        Task work(IServiceProvider sp)
        {
            var service = sp.GetRequiredService<TService>();
            return job(service);
        }

        await channel.Writer.WriteAsync(work);
    }

    internal ChannelReader<Func<IServiceProvider, Task>> Reader => channel.Reader;
}

public class JobBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<JobBackgroundService> logger,
    JobCreator jobCreator,
    int maxDegreeOfParallelism = 20)
    : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism, maxDegreeOfParallelism);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = jobCreator.Reader;
        var runningTasks = new List<Task>();

        await foreach (var job in reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken);

            var task = ProcessJobAsync(job, stoppingToken);
            runningTasks.Add(task);
            runningTasks.RemoveAll(t => t.IsCompleted);
        }

        await Task.WhenAll(runningTasks);
    }

    private async Task ProcessJobAsync(Func<IServiceProvider, Task> job, CancellationToken token)
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
    }
}