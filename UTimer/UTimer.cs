using System.Linq.Expressions;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace UTimer;

/// <summary>
/// Provides methods for creating and scheduling jobs.
/// </summary>
public static class JobCreator
{
    /// <summary>
    /// Creates a one-time job.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="period">The delay before the action is executed.</param>
    public static void CreateJob([NotNull] Expression<Action> action, TimeSpan? period = null)
    {
        if (action is null) throw new ActionNullException("Action cannot be null.");

        CreateTimerByJob(Callback.CreateTimerCallback(action), period);
    }

    /// <summary>
    /// Creates a one-time job with a specified type parameter.
    /// </summary>
    /// <typeparam name="T">The type parameter for the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="period">The delay before the action is executed.</param>
    public static void CreateJob<T>([NotNull] Expression<Action<T>> action, TimeSpan? period = null) where T : notnull
    {
        if (action is null) throw new ActionNullException("Action cannot be null.");

        CreateTimerByJob(Callback.CreateTimerCallback(action), period);
    }

    /// <summary>
    /// Creates a recurring job.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="period">The interval between executions.</param>
    /// <param name="cronExpression">The cron expression for scheduling.</param>
    public static void CreateRecurringJob([NotNull] Expression<Action> action, TimeSpan? period = null, string? cronExpression = null)
    {
        if (action is null) throw new ActionNullException("Action cannot be null.");

        if (period is null && cronExpression is null) throw new PeriodAndCronNullException("Period or cron expression needs to be given for repetition.");

        CreateTimerByRecurringJob(Callback.CreateTimerCallback(action), period, cronExpression);
    }

    /// <summary>
    /// Creates a recurring job with a specified type parameter.
    /// </summary>
    /// <typeparam name="T">The type parameter for the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="period">The interval between executions.</param>
    /// <param name="cronExpression">The cron expression for scheduling.</param>
    public static void CreateRecurringJob<T>([NotNull] Expression<Action<T>> action, TimeSpan? period = null, string? cronExpression = null) where T : notnull
    {
        if (action is null) throw new ActionNullException("Action cannot be null.");

        if (period is null && cronExpression is null) throw new PeriodAndCronNullException("Period or cron expression needs to be given for repetition.");

        CreateTimerByRecurringJob(Callback.CreateTimerCallback(action), period, cronExpression);
    }

    /// <summary>
    /// Creates a one-time timer for a job.
    /// </summary>
    /// <param name="callback">The callback to execute.</param>
    /// <param name="period">The delay before execution.</param>
    /// <returns>A Timer instance.</returns>
    private static Timer CreateTimerByJob(TimerCallback callback, TimeSpan? period) => new(callback, null, period ?? TimeSpan.FromSeconds(0), Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Creates a recurring timer for a job.
    /// </summary>
    /// <param name="callback">The callback to execute.</param>
    /// <param name="period">The interval between executions.</param>
    /// <param name="cronExpression">The cron expression for scheduling.</param>
    private static void CreateTimerByRecurringJob(TimerCallback callback, TimeSpan? period, string? cronExpression)
    {
        if (period is not null)
            _ = new Timer(callback, null, TimeSpan.Zero, period!.Value);
        else
            RunScheduledJob(callback, cronExpression!);
    }

    /// <summary>
    /// Schedules and runs a job based on a cron expression.
    /// </summary>
    /// <param name="callback">The callback to execute.</param>
    /// <param name="cronExpression">The cron expression for scheduling.</param>
    private static void RunScheduledJob(TimerCallback callback, string cronExpression)
    {
        void ScheduleNextRun()
        {
            var nextRun = Cron.GetNextOccurrence(cronExpression, DateTime.Now);
            var delay = nextRun - DateTime.Now;

            var timer = new Timer(state =>
            {
                callback(state);
                ScheduleNextRun();
            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        ScheduleNextRun();
    }
}

/// <summary>
/// Manages a queue of jobs to be executed sequentially.
/// </summary>
public class JobQueue
{
    private readonly ConcurrentQueue<(Guid, Func<Task>)> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Task> _jobStatuses = new();
    private bool _isProcessing;

    /// <summary>
    /// Enqueues a job with a specified delay.
    /// </summary>
    /// <typeparam name="T">The type parameter for the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <param name="delay">The delay before execution.</param>
    public void EnqueueJob<T>([NotNull] Expression<Action<T>> action, TimeSpan? delay = null) where T : notnull
    {
        if (action is null) throw new ActionNullException("Action cannot be null.");

        var jobId = Guid.NewGuid();
        var callback = CreateTimerCallback(action);

        async Task job()
        {
            if (delay is not null) await Task.Delay(delay!.Value);

            await callback();
            _jobStatuses[jobId] = Task.CompletedTask;
        }

        _jobs.Enqueue((jobId, job));
    }

    /// <summary>
    /// Starts processing the job queue.
    /// </summary>
    public void Start()
    {
        if (_isProcessing) return;

        _isProcessing = true;
        ProcessNextJob();
    }

    /// <summary>
    /// Processes the next job in the queue.
    /// </summary>
    private void ProcessNextJob()
    {
        if (_jobs.TryDequeue(out var job))
        {
            var (jobId, jobFunc) = job;
            var task = jobFunc().ContinueWith(_ => ProcessNextJob());
            _jobStatuses[jobId] = task;
        }
        else
            _isProcessing = false;
    }

    /// <summary>
    /// Creates a timer callback from an action.
    /// </summary>
    /// <typeparam name="T">The type parameter for the action.</typeparam>
    /// <param name="action">The action to execute.</param>
    /// <returns>A function that executes the action.</returns>
    private static Func<Task> CreateTimerCallback<T>(Expression<Action<T>> action) where T : notnull => async () =>
    {
        action.Compile()(ServiceProviderContainer.ServiceProvider.GetRequiredService<T>());
        await Task.CompletedTask;
    };
}

/// <summary>
/// Provides utility methods for generating cron expressions and calculating next occurrences.
/// </summary>
public static class Cron
{
    public static string Minutely() => "* * * * *";

    public static string Hourly() => Hourly(0);
    public static string Hourly(int minute) => string.Format("{0} * * * *", minute);

    public static string Daily(int? hour = null) => hour is null ? Daily(0, 0) : Daily(hour!.Value, 0);
    public static string Daily(int hour, int minute) => string.Format("{0} {1} * * *", minute, hour);

    public static string Weekly(DayOfWeek? dayOfWeek = null) => dayOfWeek is null ? Weekly(DayOfWeek.Monday, 0) : Weekly(dayOfWeek!.Value, 0);
    public static string Weekly(DayOfWeek dayOfWeek, int hour) => Weekly(dayOfWeek, hour, 0);
    public static string Weekly(DayOfWeek dayOfWeek, int hour, int minute) => string.Format("{0} {1} * * {2}", minute, hour, (int)dayOfWeek);

    public static string Monthly(int? day = null) => day is null ? Monthly(1, 0) : Monthly(day!.Value, 0);
    public static string Monthly(int day, int hour) => Monthly(day, hour, 0);
    public static string Monthly(int day, int hour, int minute) => string.Format("{0} {1} {2} * *", minute, hour, day);

    public static string Yearly(int? month = null) => month is null ? Yearly(1, 1) : Yearly(month!.Value, 1);
    public static string Yearly(int month, int day) => Yearly(month, day, 0);
    public static string Yearly(int month, int day, int hour) => Yearly(month, day, hour, 0);
    public static string Yearly(int month, int day, int hour, int minute) => string.Format("{0} {1} {2} {3} *", minute, hour, day, month);

    public static DateTime GetNextOccurrence(string cronExpression, DateTime baseTime)
    {
        var parts = cronExpression.Split(' ');
        if (parts.Length != 5)
            throw new ArgumentException("Invalid cron expression format. Expected format: 'm h d M w'");

        int minute = ParseCronField(parts[0], 0, 59, baseTime.Minute);
        int hour = ParseCronField(parts[1], 0, 23, baseTime.Hour);
        int day = ParseCronField(parts[2], 1, 31, baseTime.Day);
        int month = ParseCronField(parts[3], 1, 12, baseTime.Month);
        int dayOfWeek = ParseCronField(parts[4], 0, 6, (int)baseTime.DayOfWeek);

        var nextOccurrence = new DateTime(baseTime.Year, month, day, hour, minute, 0);

        // dayOfWeek ile uyumlu bir gün bulana kadar döngüye devam et
        while (nextOccurrence.DayOfWeek != (DayOfWeek)dayOfWeek || nextOccurrence <= baseTime)
            nextOccurrence = nextOccurrence.AddMinutes(1);

        return nextOccurrence;
    }

    private static int ParseCronField(string field, int minValue, int maxValue, int currentValue)
    {
        if (field == "*")
            return currentValue;

        if (field.Contains("*/"))
        {
            int interval = int.Parse(field.Substring(2));
            return ((currentValue / interval) + 1) * interval;
        }

        if (int.TryParse(field, out int value) && value >= minValue && value <= maxValue)
            return value;

        throw new ArgumentException($"Invalid cron field value: {field}");
    }
}

/// <summary>
/// Provides methods for creating timer callbacks from expressions.
/// </summary>
public static class Callback
{
    /// <summary>
    /// Creates a TimerCallback from a given expression.
    /// </summary>
    /// <param name="action">The expression representing the action to execute.</param>
    /// <returns>A TimerCallback that executes the compiled action.</returns>
    public static TimerCallback CreateTimerCallback(Expression<Action> action) => state => { action.Compile()(); };

    /// <summary>
    /// Creates a TimerCallback from a given expression with a type parameter.
    /// </summary>
    /// <typeparam name="T">The type parameter for the action.</typeparam>
    /// <param name="action">The expression representing the action to execute.</param>
    /// <returns>A TimerCallback that executes the compiled action with the required service of type T.</returns>
    public static TimerCallback CreateTimerCallback<T>(Expression<Action<T>> action) where T : notnull =>
         state => { action.Compile()(ServiceProviderContainer.ServiceProvider.GetRequiredService<T>()); };
}

/// <summary>
/// Provides extension methods for registering UTimer services in the IServiceCollection.
/// </summary>
public static class UTimerExtensions
{
    /// <summary>
    /// Adds UTimer services to the IServiceCollection.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <returns>The IServiceCollection for chaining.</returns>
    public static IServiceCollection AddUTimer([NotNull] this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        ServiceProviderContainer.ServiceProvider = services.BuildServiceProvider();
        return services;
    }
}

/// <summary>
/// Holds a reference to the IServiceProvider used for resolving dependencies.
/// </summary>
public static class ServiceProviderContainer
{
    /// <summary>
    /// Gets or sets the IServiceProvider instance.
    /// </summary>
    public static IServiceProvider ServiceProvider { get; set; } = null!;
}

/// <summary>
/// Exception thrown when an action is null.
/// </summary>
public class ActionNullException(string message) : Exception(message) { }

/// <summary>
/// Exception thrown when both period and cron expression are null.
/// </summary>
public class PeriodAndCronNullException(string message) : Exception(message) { }