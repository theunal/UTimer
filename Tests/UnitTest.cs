using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using UTimer;

namespace Tests;

public class DummyService
{
    public List<string> Logs { get; } = new List<string>();

    public Task DoWork(string message)
    {
        lock (Logs)
        {
            Logs.Add(message);
        }
        return Task.CompletedTask;
    }
}

public class ExceptionService
{
    public Task FailWork()
    {
        throw new InvalidOperationException("Fail!");
    }
}

[TestFixture]
public class UTimerTests
{
    private ServiceProvider _provider = null!;
    private JobCreator _jobCreator = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Kendi dummy servisimizi ve job timer'ı ekleyelim
        services.AddSingleton<DummyService>();
        services.AddUTimer(maxDegreeOfParallelism: 5, maxQueueSize: 100);

        _provider = services.BuildServiceProvider();

        _jobCreator = _provider.GetRequiredService<JobCreator>();
    }

    [TearDown]
    public void TearDown()
    {
        _provider?.Dispose();
    }

    [Test]
    public void AddUTimer_RegistersServicesCorrectly()
    {
        Assert.That(_jobCreator, Is.Not.Null);
        var channel = _provider.GetService<System.Threading.Channels.Channel<Func<IServiceProvider, Task>>>();
        Assert.That(channel, Is.Not.Null);
    }

    [Test]
    public void EnqueueAsync_NullJob_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await _jobCreator.EnqueueAsync<DummyService>(null!);
        });
    }

    [Test]
    public async Task EnqueueAsync_ShouldQueueAndExecuteJob()
    {
        var dummy = _provider.GetRequiredService<DummyService>();

        await _jobCreator.EnqueueAsync<DummyService>(svc => svc.DoWork("hello"));

        var reader = _jobCreator.Reader;

        var job = await reader.ReadAsync();

        await job(_provider);

        Assert.That(dummy.Logs.Count, Is.EqualTo(1));
        Assert.That(dummy.Logs[0], Is.EqualTo("hello"));
    }
}
