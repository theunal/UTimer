using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace UTimer;

public static class UTimerExtensions
{
    public static IServiceCollection AddUTimer(this IServiceCollection services, int maxDegreeOfParallelism = 20)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService(sp => new JobBackgroundService(
            sp,
            sp.GetRequiredService<ILogger<JobBackgroundService>>(),
            maxDegreeOfParallelism));

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

public class JobBackgroundService(IServiceProvider serviceProvider, ILogger<JobBackgroundService> logger, int maxDegreeOfParallelism = 20) : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(maxDegreeOfParallelism, maxDegreeOfParallelism);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var runningTasks = new List<Task>();

        await foreach (var job in JobCreator.Reader.ReadAllAsync(stoppingToken))
        {
            await _semaphore.WaitAsync(stoppingToken); // boş worker varsa devam et yoksa bekle

            // İş başlat
            var task = ProcessJobAsync(job, stoppingToken);

            // Listeye ekle
            runningTasks.Add(task);

            // Tamamlanmış iş varsa temizle
            runningTasks.RemoveAll(t => t.IsCompleted);

            // Eğer çok fazla task varsa, en az biri bitene kadar bekle (isteğe bağlı)
            if (runningTasks.Count >= maxDegreeOfParallelism)
            {
                var completed = await Task.WhenAny(runningTasks);
                runningTasks.Remove(completed);
            }
        }

        // Kuyruk kapanınca, çalışan işleri bekle
        await Task.WhenAll(runningTasks);
    }

    private async Task ProcessJobAsync(Func<IServiceProvider, Task> job, CancellationToken stoppingToken)
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
            _semaphore.Release(); // İş bitti, worker hakkını geri ver
        }
    }
}