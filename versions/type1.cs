namespace UTimer;

public static class UTimerExtensions
{
    public static IServiceCollection AddUTimer([NotNull] this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<JobBackgroundService>();

        return services;
    }
}

public static class JobCreator
{
    private static class JobQueue
    {
        public static readonly Channel<Func<IServiceProvider, Task>> _queue = Channel.CreateUnbounded<Func<IServiceProvider, Task>>();
        internal static ChannelReader<Func<IServiceProvider, Task>> Reader => _queue.Reader;
    }

    internal static ChannelReader<Func<IServiceProvider, Task>> Reader => JobQueue.Reader;

    public static void Enqueue<TService>(Func<TService, Task> job) where TService : class
    {
        ArgumentNullException.ThrowIfNull(job);

        Task work(IServiceProvider sp)
        {
            var service = sp.GetRequiredService<TService>();
            return job(service);
        }

        JobQueue._queue.Writer.TryWrite(work);
    }
}

public class JobBackgroundService(IServiceProvider serviceProvider, ILogger<JobBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in JobCreator.Reader.ReadAllAsync(stoppingToken))
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
        }
    }
}