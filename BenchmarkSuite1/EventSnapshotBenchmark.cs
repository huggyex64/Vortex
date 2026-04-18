using BenchmarkDotNet.Attributes;
using System;
using Vortex;

namespace Vortex.Benchmarks
{
    [MemoryDiagnoser]
    public class EventSnapshotBenchmark
    {
        private enum TestEvents
        {
            OnUpdate,
            OnRender
        }

        private EventManager<TestEvents> _manager = null!;

        [GlobalSetup]
        public void Setup()
        {
            _manager = new EventManager<TestEvents>();

            // Add 50 handlers to simulate realistic usage
            // Arrays larger than 85KB go to LOH
            for (int i = 0; i < 50; i++)
            {
                _manager.AddNewDelegate<TestArgs>(TestEvents.OnUpdate, args => { /* no-op */ });
            }
        }

        [Benchmark]
        public void AddRemoveHandlers()
        {
            // Add and remove handlers, triggering snapshot rebuilds
            var container = _manager.AddNewDelegate<TestArgs>(TestEvents.OnUpdate, args => { });
            _manager.RemoveDelegate(container);
        }

        [Benchmark]
        public void InvokeEvent()
        {
            _manager.InvokeEvent(TestEvents.OnUpdate, new TestArgs { Value = 42 });
        }

        public struct TestArgs
        {
            public int Value;
        }
    }
}