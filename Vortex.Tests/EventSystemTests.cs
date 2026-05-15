using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex;
using Xunit;

namespace Vortex.Tests;

public enum TestEvents
{
    Simple,
    Cancellable,
    Async,
    Priority
}

public record struct SimpleArgs(string Message);

public class CancellableArgs : ICancellable
{
    public string Message { get; set; }
    public bool Cancelled { get; set; }
    public CancellableArgs(string message) => Message = message;
}

public enum ContractEvents
{
    [EventArgs(typeof(SimpleArgs))]
    ValidEvent
}

public class EventSystemTests : IDisposable
{
    private readonly System.Collections.Generic.List<IDisposable> _disposables = new();

    private EventManager<T> CreateManager<T>(bool global = false) where T : struct, Enum
    {
        var manager = new EventManager<T>(global);
        _disposables.Add(manager);
        return manager;
    }

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    [Fact]
    public void SimpleEvent_ShouldInvokeHandler()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        string? received = null;
        
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, args => received = args.Message);

        // Act
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("Hello"));

        // Assert
        received.Should().Be("Hello");
    }

    [Fact]
    public async Task AsyncEvent_ShouldInvokeHandler()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        string? received = null;

        manager.AddNewAsyncDelegate<SimpleArgs>(TestEvents.Async, async args =>
        {
            await Task.Yield();
            received = args.Message;
        });

        // Act
        await manager.InvokeEventAsync(TestEvents.Async, new SimpleArgs("Hello Async"));

        // Assert
        received.Should().Be("Hello Async");
    }

    [Fact]
    public void Priority_ShouldInvokeInOrder()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        var results = new System.Collections.Generic.List<int>();

        manager.AddNewDelegate<SimpleArgs>(TestEvents.Priority, _ => results.Add(1), priority: (EventPriority)10);
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Priority, _ => results.Add(2), priority: (EventPriority)20);
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Priority, _ => results.Add(3), priority: (EventPriority)5);

        // Act
        manager.InvokeEvent(TestEvents.Priority, new SimpleArgs("Test"));

        // Assert
        results.Should().Equal(3, 1, 2);
    }

    [Fact]
    public void Cancellation_ShouldStopInvocation()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;

        manager.AddNewDelegate<CancellableArgs>(TestEvents.Cancellable, args => 
        {
            callCount++;
            args.Cancelled = true;
        }, priority: (EventPriority)1);

        manager.AddNewDelegate<CancellableArgs>(TestEvents.Cancellable, args => 
        {
            callCount++;
        }, priority: (EventPriority)2);

        // Act
        var args = new CancellableArgs("Cancel me");
        manager.InvokeEvent(TestEvents.Cancellable, args);

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void DisposableSubscription_ShouldUnsubscribeOnDispose()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        var subscription = manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => callCount++);

        // Act
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("1"));
        subscription.Dispose();
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("2"));

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void ExceptionInHandler_ShouldNotStopOtherHandlers()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => throw new Exception("Boom"), priority: (EventPriority)1);
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => callCount++, priority: (EventPriority)2);

        // Act
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("Test"));

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void GlobalInstances_LastGlobalInstance_ShouldReturnEnabledOne()
    {
        // Arrange
        var manager1 = CreateManager<TestEvents>(global: true);
        var manager2 = CreateManager<TestEvents>(global: true);

        // Act & Assert
        EventManager<TestEvents>.LastGlobalInstance.Should().Be(manager2);

        manager2.Enabled = false;
        EventManager<TestEvents>.LastGlobalInstance.Should().Be(manager1);

        manager1.Enabled = false;
        EventManager<TestEvents>.LastGlobalInstance.Should().BeNull();
    }

    [Fact]
    public void TagManagement_ShouldEnableDisableHandlers()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => callCount++, tags: new[] { "tag1" });

        // Act
        manager.DisableByTag("tag1");
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("1"));
        
        manager.EnableByTag("tag1");
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("2"));

        manager.RemoveByTag("tag1");
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("3"));

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void Batching_ShouldDeferSnapshotRebuild()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        Action<SimpleArgs> handler = _ => callCount++;

        // Act
        manager.BeginBatch();
        manager.AddNewDelegate(TestEvents.Simple, handler);
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("1")); 
        
        manager.EndBatch();
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("2"));

        // Assert
        callCount.Should().Be(1); // Only the second one should trigger
    }

    [Fact]
    public void AllowMultiple_False_ShouldNotAddSameHandlerTwice()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        Action<SimpleArgs> handler = _ => callCount++;

        // Act
        manager.AddNewDelegate(TestEvents.Simple, handler, allowMultiple: false);
        manager.AddNewDelegate(TestEvents.Simple, handler, allowMultiple: false);
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("Test"));

        // Assert
        callCount.Should().Be(1);
    }

    [Fact]
    public void AllowMultiple_True_ShouldAddSameHandlerTwice()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        int callCount = 0;
        Action<SimpleArgs> handler = _ => callCount++;

        // Act
        manager.AddNewDelegate(TestEvents.Simple, handler, allowMultiple: true);
        manager.AddNewDelegate(TestEvents.Simple, handler, allowMultiple: true);
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("Test"));

        // Assert
        callCount.Should().Be(2);
    }

    [Fact]
    public void EventArgsContract_ShouldThrowOnWrongType()
    {
        // Arrange
        var manager = CreateManager<ContractEvents>();
        
        // Act & Assert
        Action act = () => manager.AddNewDelegate<CancellableArgs>(ContractEvents.ValidEvent, _ => { });
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*handler registered with 'CancellableArgs' but the event declares 'SimpleArgs'*");
    }

    [Fact]
    public void ThreadSafety_ConcurrentSubscriptionsAndInvocations()
    {
        // Arrange
        var manager = CreateManager<TestEvents>();
        const int threadCount = 10;
        const int iterations = 100;
        var tasks = new System.Collections.Generic.List<Task>();
        int totalCalls = 0;

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    using (manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => System.Threading.Interlocked.Increment(ref totalCalls)))
                    {
                        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("Test"));
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - If we reached here without exception, it's a good sign.
        totalCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GlobalInvokeEvent_ShouldInvokeAcrossAllGlobalManagers()
    {
        // Arrange
        var manager1 = CreateManager<TestEvents>(global: true);
        var manager2 = CreateManager<TestEvents>(global: true);
        var manager3 = CreateManager<TestEvents>(global: false);
        int calls = 0;
        
        manager1.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => calls++);
        manager2.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => calls++);
        manager3.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => calls++);

        // Act
        EventManager<TestEvents>.GlobalInvokeEvent(TestEvents.Simple, new SimpleArgs("Test"));

        // Assert
        calls.Should().Be(2);
    }

    [Fact]
    public async Task WaitForEventAsync_ShouldCompleteOnNextInvoke()
    {
        var manager = CreateManager<TestEvents>();

        Task<SimpleArgs> waiter = manager.WaitForEventAsync<SimpleArgs>(TestEvents.Simple);

        waiter.IsCompleted.Should().BeFalse();
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("hello"));

        SimpleArgs result = await waiter;
        result.Message.Should().Be("hello");
    }

    [Fact]
    public async Task WaitForEventAsync_ShouldAutoUnsubscribeAfterCompletion()
    {
        var manager = CreateManager<TestEvents>();
        int sideHandlerCalls = 0;
        manager.AddNewDelegate<SimpleArgs>(TestEvents.Simple, _ => sideHandlerCalls++);

        Task<SimpleArgs> waiter = manager.WaitForEventAsync<SimpleArgs>(TestEvents.Simple);
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("first"));
        await waiter;

        // Second invoke should not affect the completed waiter, but should still hit the side handler.
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("second"));
        sideHandlerCalls.Should().Be(2);

        // The waiter task should still hold the first result, not the second.
        waiter.Result.Message.Should().Be("first");
    }

    [Fact]
    public async Task WaitForEventAsync_WithPredicate_ShouldIgnoreNonMatchingInvokes()
    {
        var manager = CreateManager<TestEvents>();

        Task<SimpleArgs> waiter = manager.WaitForEventAsync<SimpleArgs>(
            TestEvents.Simple, args => args.Message == "match");

        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("skip-1"));
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("skip-2"));
        waiter.IsCompleted.Should().BeFalse();

        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("match"));
        SimpleArgs result = await waiter;
        result.Message.Should().Be("match");
    }

    [Fact]
    public async Task WaitForEventAsync_ShouldCancelOnTokenCancellation()
    {
        var manager = CreateManager<TestEvents>();
        using CancellationTokenSource cts = new();

        Task<SimpleArgs> waiter = manager.WaitForEventAsync<SimpleArgs>(TestEvents.Simple, cts.Token);
        cts.Cancel();

        Func<Task> act = async () => await waiter;
        await act.Should().ThrowAsync<TaskCanceledException>();

        // After cancellation, invoking should not throw or affect anything.
        manager.InvokeEvent(TestEvents.Simple, new SimpleArgs("ignored"));
        waiter.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public void WaitForEventAsync_AlreadyCancelledToken_ShouldReturnCanceledTask()
    {
        var manager = CreateManager<TestEvents>();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Task<SimpleArgs> waiter = manager.WaitForEventAsync<SimpleArgs>(TestEvents.Simple, cts.Token);

        waiter.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForEventAsync_Parameterless_ShouldCompleteOnNextInvoke()
    {
        var manager = CreateManager<TestEvents>();

        Task waiter = manager.WaitForEventAsync(TestEvents.Simple);

        waiter.IsCompleted.Should().BeFalse();
        manager.InvokeEvent(TestEvents.Simple);

        await waiter;
        waiter.Status.Should().Be(TaskStatus.RanToCompletion);
    }

    [Fact]
    public void WaitForEventAsync_ShouldThrowOnContractMismatch()
    {
        var manager = CreateManager<ContractEvents>();

        Action act = () => manager.WaitForEventAsync<CancellableArgs>(ContractEvents.ValidEvent);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WaitForEventAsync invoked with 'CancellableArgs' but the event declares 'SimpleArgs'*");
    }
}
