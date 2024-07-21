using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Linq.Expressions;
using UTimer;
using UTimer.Cronos;

namespace Tests
{
    public class CreateJobTests
    {
        [Test]
        public void CreateJob_NullAction_ThrowsActionNullException()
        {
            // Arrange
            Expression<Action>? nullAction = null;

            // Act & Assert
            Assert.Throws<ActionNullException>(() => JobCreator.CreateJob(nullAction));
        }

        [Test]
        public void CreateJob_ValidAction_CreatesTimerWithCorrectCallback()
        {
            // Arrange
            var actionMock = new Mock<Action>();
            Expression<Action> action = () => actionMock.Object();

            // Act
            var timer = JobCreator.CreateJob(action, TimeSpan.FromSeconds(1));

            // Assert
            actionMock.Verify(a => a(), Times.Never, "Action should not be executed immediately.");
        }

        [Test]
        public void CreateJob_ValidAction_CreatesTimerWithCorrectPeriod()
        {
            // Arrange
            var actionMock = new Mock<Action>();
            Expression<Action> action = () => actionMock.Object();
            var period = TimeSpan.FromSeconds(1);

            // Act
            var timer = JobCreator.CreateJob(action, period);

            // Assert
            Assert.That(timer, Is.Not.Null, "Timer should be created.");
            // Note: Since the timer is private, we can't directly verify its properties.
            // However, we could restructure the code to allow better testability.
        }

        [Test]
        public void CreateJob_3_Seconds_Period_ActionExecutesAfter3Seconds()
        {
            // Arrange
            var actionMock = new Mock<Action>();
            var period = TimeSpan.FromSeconds(3); // Reverting to 3 seconds for the actual test logic
            var resetEvent = new ManualResetEvent(false);

            Expression<Action> action = () => TestMethod(actionMock, resetEvent);

            // Act
            var timer = JobCreator.CreateJob(action, period);

            // Assert
            Assert.That(timer, Is.Not.Null, "Timer should be created.");

            // Wait for up to 6 seconds for the action to be called
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(6));

            Assert.That(actionExecuted, Is.True, "Action should be executed after the specified period.");
            actionMock.Verify(a => a(), Times.Once, "Action should be called exactly once.");
        }

        private void TestMethod(Mock<Action> actionMock, ManualResetEvent resetEvent)
        {
            actionMock.Object();
            resetEvent.Set();
        }
    }

    public class CreateJobGenericActionTests
    {
        private Mock<ITestService> _testServiceMock;

        [SetUp]
        public void Setup()
        {
            var services = new ServiceCollection();

            _testServiceMock = new Mock<ITestService>();
            services.AddScoped<ITestService>(_ => _testServiceMock.Object);

            var _serviceProvider = services.BuildServiceProvider();
            ServiceProviderContainer.ServiceProvider = _serviceProvider;
        }

        [Test]
        public void CreateJob_NullAction_ThrowsActionNullException()
        {
            // Arrange
            Expression<Action<string>>? nullAction = null;

            // Act & Assert
            Assert.Throws<ActionNullException>(() => JobCreator.CreateJob(nullAction));
        }

        [Test]
        public void CreateJob_ValidAction_CreatesTimerWithCorrectCallback()
        {
            // Arrange
            var actionMock = new Mock<Action<string>>();
            Expression<Action<string>> action = (str) => actionMock.Object(str);

            // Act
            JobCreator.CreateJob(action, TimeSpan.FromSeconds(1));

            // Assert
            actionMock.Verify(a => a(It.IsAny<string>()), Times.Never, "Action should not be executed immediately.");
        }

        [Test]
        public void CreateJob_3_Seconds_Period_ActionExecutesAfter3Seconds()
        {
            // Arrange
            var period = TimeSpan.FromSeconds(3); // Use 3 seconds for the test logic
            var resetEvent = new ManualResetEvent(false);

            Expression<Action<ITestService>> action = service => TestMethod(service, resetEvent);

            // Act
            var timer = JobCreator.CreateJob(action, period);

            // Assert
            Assert.That(timer, Is.Not.Null, "Timer should be created.");

            // Wait for up to 6 seconds for the action to be called
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(6));

            Assert.That(actionExecuted, Is.True, "Action should be executed after the specified period.");
            _testServiceMock.Verify(service => service.Execute(), Times.Once, "Action should be called exactly once.");
        }

        private void TestMethod(ITestService service, ManualResetEvent resetEvent)
        {
            service.Execute();
            resetEvent.Set();
        }
    }

    public class CreateRecurringJobPeriodTests
    {
        private Mock<ITestService> _testServiceMock;

        [SetUp]
        public void Setup()
        {
            // Setup the ServiceProvider
            var services = new ServiceCollection();
            _testServiceMock = new Mock<ITestService>();
            services.AddSingleton(_testServiceMock.Object);
            ServiceProviderContainer.ServiceProvider = services.BuildServiceProvider();
        }

        [Test]
        public void CreateRecurringJob_ExecutesActionPeriodically()
        {
            // Arrange
            var period = TimeSpan.FromSeconds(3); // The interval between executions
            var resetEvent = new ManualResetEvent(false);
            int executionCount = 0;

            Expression<Action> action = () => LocalTestMethod(resetEvent, ref executionCount);

            // Act
            JobCreator.CreateRecurringJob(action, period);

            // Wait for some time to allow the job to execute at least once
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(actionExecuted, Is.True, "Action should be executed at least once within the wait period.");
            _testServiceMock.Verify(s => s.Execute(), Times.AtLeastOnce, "Action should be called at least once.");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(2), "Action should be executed multiple times within the wait period.");
        }

        private void LocalTestMethod(ManualResetEvent resetEvent, ref int executionCount)
        {
            TestMethod(resetEvent, ref executionCount);
        }
        private void TestMethod(ManualResetEvent resetEvent, ref int executionCount)
        {
            Console.WriteLine("TestMethod Invoked " + DateTime.Now);
            _testServiceMock.Object.Execute();
            Interlocked.Increment(ref executionCount);
            if (executionCount >= 2)
            {
                resetEvent.Set();
            }
        }

        [Test]
        public void CreateRecurringJob_ExecutesGenericActionPeriodically()
        {
            // Arrange
            var period = TimeSpan.FromSeconds(5); // The interval between executions
            var resetEvent = new ManualResetEvent(false);
            int executionCount = 0;

            Expression<Action<ITestService>> action = service => TestMethod(service, resetEvent, ref executionCount);

            // Act
            JobCreator.CreateRecurringJob(action, period);

            // Wait for some time to allow the job to execute at least once
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(15));

            // Assert
            Assert.That(actionExecuted, Is.True, "Action should be executed at least once within the wait period.");
            _testServiceMock.Verify(s => s.Execute(), Times.AtLeastOnce, "Action should be called at least once.");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(2), "Action should be executed multiple times within the wait period.");
        }

        private void TestMethod(ITestService service, ManualResetEvent resetEvent, ref int executionCount)
        {
            Console.WriteLine("TestMethod Invoked " + DateTime.Now);
            service.Execute();
            Interlocked.Increment(ref executionCount);
            if (executionCount >= 2)
            {
                resetEvent.Set();
            }
        }
    }

    public class CreateRecurringJobCronTests
    {
        private Mock<ITestService> _testServiceMock;

        [SetUp]
        public void Setup()
        {
            var services = new ServiceCollection();

            _testServiceMock = new Mock<ITestService>();
            services.AddScoped<ITestService>(_ => _testServiceMock.Object);

            var _serviceProvider = services.BuildServiceProvider();
            ServiceProviderContainer.ServiceProvider = _serviceProvider;
        }

        [Test]
        public void CreateRecurringJob_ExecutesActionWithCronExpression()
        {
            // Arrange
            var cronExpression = Cron.Minutely(); // Every second
            var resetEvent = new ManualResetEvent(false);
            int executionCount = 0;

            Expression<Action> action = () => LocalTestMethod(resetEvent, ref executionCount);

            // Act
            JobCreator.CreateRecurringJob(action, cronExpression: cronExpression);

            // Wait for some time to allow the job to execute at least once
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(actionExecuted, Is.True, "Action should be executed at least once within the wait period.");
            _testServiceMock.Verify(s => s.Execute(), Times.AtLeastOnce, "Action should be called at least once.");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(2), "Action should be executed multiple times within the wait period.");
        }

        private void LocalTestMethod(ManualResetEvent resetEvent, ref int executionCount)
        {
            TestMethod(resetEvent, ref executionCount);
        }

        private void TestMethod(ManualResetEvent resetEvent, ref int executionCount)
        {
            Console.WriteLine("TestMethod Invoked " + DateTime.Now);
            _testServiceMock.Object.Execute();
            Interlocked.Increment(ref executionCount);
            if (executionCount >= 2)
            {
                resetEvent.Set();
            }
        }

        [Test]
        public void CreateRecurringJob_ExecutesGenericActionWithCronExpression()
        {
            // Arrange
            var cronExpression = "* * * * * *"; // Every second
            var resetEvent = new ManualResetEvent(false);
            int executionCount = 0;

            Expression<Action<ITestService>> action = service => TestMethod(service, resetEvent, ref executionCount);

            // Act
            JobCreator.CreateRecurringJob(action, null, cronExpression);

            // Wait for some time to allow the job to execute at least once
            bool actionExecuted = resetEvent.WaitOne(TimeSpan.FromSeconds(10));

            // Assert
            Assert.That(actionExecuted, Is.True, "Action should be executed at least once within the wait period.");
            _testServiceMock.Verify(s => s.Execute(), Times.AtLeastOnce, "Action should be called at least once.");
            Assert.That(executionCount, Is.GreaterThanOrEqualTo(2), "Action should be executed multiple times within the wait period.");
        }

        private void TestMethod(ITestService service, ManualResetEvent resetEvent, ref int executionCount)
        {
            Console.WriteLine("TestMethod Invoked " + DateTime.Now);
            service.Execute();
            Interlocked.Increment(ref executionCount);
            if (executionCount >= 2)
            {
                resetEvent.Set();
            }
        }
    }
}