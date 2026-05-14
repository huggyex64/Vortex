using Vortex;
using Xunit;
using FluentAssertions;

namespace Vortex.Tests;

[EventDomain(Global = true)]
public static partial class TestDomain
{
    private static readonly EventKey _OnTestEvent = new();
    
    [EventArgs(typeof(SimpleArgs))]
    private static readonly EventKey _OnArgsEvent = new();

    private static readonly EventKey _OnAsyncEvent = new();
}

public class GeneratedEventTests
{
    [Fact]
    public void GeneratedDomain_Basic_ShouldWork()
    {
        bool invoked = false;
        TestDomain.OnTestEvent += () => invoked = true;
        TestDomain.InvokeOnTestEvent();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void GeneratedDomain_WithArgs_ShouldWork()
    {
        string? received = null;
        TestDomain.OnArgsEvent += args => received = args.Message;
        TestDomain.InvokeOnArgsEvent(new SimpleArgs("Hello"));
        received.Should().Be("Hello");
    }

    [Fact]
    public async Task GeneratedDomain_Async_ShouldWork()
    {
        bool invoked = false;
        TestDomain.OnAsyncEvent += async () => 
        {
            await Task.Yield();
            invoked = true;
        };
        await TestDomain.InvokeOnAsyncEventAsync();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void GlobalInvoke_ShouldWork()
    {
        bool invoked = false;
        TestDomain.OnTestEvent += () => invoked = true;
        
        TestDomain.GlobalInvokeOnTestEvent();
        invoked.Should().BeTrue();
    }

    [Fact]
    public void SubscribeOnce_ShouldWork()
    {
        int callCount = 0;
        TestDomain.SubscribeOnTestEventOnce(() => callCount++);
        
        TestDomain.InvokeOnTestEvent();
        TestDomain.InvokeOnTestEvent();
        
        callCount.Should().Be(1);
    }
}
