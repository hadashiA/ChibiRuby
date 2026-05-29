using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace ChibiRuby.Internals;

public class MultiConsumerValueTaskNotifier<T>
{
    class WaiterNode : IValueTaskSource<T>
    {
        static readonly ConcurrentQueue<WaiterNode> Pool = new();

        public static WaiterNode Rent(short tag)
        {
            if (!Pool.TryDequeue(out var node))
            {
                node = new WaiterNode();
            }
            node.Tag = tag;
            return node;
        }

        public CancellationTokenRegistration CancellationTokenRegistration { get; set; }
        ManualResetValueTaskSourceCore<T> core;

        public short Tag { get; private set; }
        public short Version => core.Version;

        WaiterNode()
        {
            core.RunContinuationsAsynchronously = false;
        }

        public void SetResult(T result)
        {
            core.SetResult(result);
        }

        public void SetException(Exception exception)
        {
            core.SetException(exception);
        }

        public void SetCanceled()
        {
            SetException(new OperationCanceledException(CancellationTokenRegistration.Token));
        }

        void Return()
        {
            CancellationTokenRegistration.Dispose();
            core.Reset();
            Pool.Enqueue(this);
        }

        T IValueTaskSource<T>.GetResult(short token)
        {
            try
            {
                return core.GetResult(token);
            }
            finally
            {
                Return();
            }
        }

        ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token)
        {
            return core.GetStatus(token);
        }

        void IValueTaskSource<T>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            core.OnCompleted(continuation, state, token, flags);
        }
    }

    readonly object gate = new();
    readonly List<WaiterNode> waiters = [];

    bool hasResult;
    T? result;
    Exception? error;
    short version;

    public ValueTask<T> WaitAsync(CancellationToken cancellation)
    {
        // Create and register waiter
        var node = WaiterNode.Rent(version);
        if (cancellation.CanBeCanceled)
        {
            node.CancellationTokenRegistration = cancellation.UnsafeRegister(static state =>
            {
                ((WaiterNode)state!).SetCanceled();
            }, node);
        }

        lock (gate)
        {
            // Check if result is already available
            if (hasResult)
            {
                // Cancel the registration if it was created
                if (cancellation.CanBeCanceled)
                {
                    node.CancellationTokenRegistration.Dispose();
                }

                if (error != null)
                {
                    return new ValueTask<T>(Task.FromException<T>(error));
                }
                return new ValueTask<T>(result!);
            }

            waiters.Add(node);
            return new ValueTask<T>(node, node.Version);
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            this.hasResult = false;
            this.result = default;
            this.error = null;
            unchecked { version++; }
            waiters.Clear();
        }
    }

    public void SetResult(T result)
    {
        WaiterNode[] waiterToNotify = [];
        int waitersCount;
        short currentVersion;

        lock (gate)
        {
            currentVersion = version;
            this.hasResult = true;
            this.result = result;
            this.error = null;

            waitersCount = waiters.Count;
            if (waitersCount > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(waitersCount);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }

            // Auto-reset after notifying waiters
            this.hasResult = false;
            this.result = default;
            this.error = null;
        }

        for (var i = 0; i < waitersCount; i++)
        {
            var waiter = waiterToNotify[i];
            waiter.SetResult(result);
        }
        ArrayPool<WaiterNode>.Shared.Return(waiterToNotify);
    }

    public void SetException(Exception exception)
    {
        WaiterNode[] waiterToNotify = [];
        int waitersCount;

        lock (gate)
        {
            hasResult = true;
            this.error = exception;
            this.result = default;
            waitersCount = waiters.Count;
            if (waitersCount > 0)
            {
                waiterToNotify = ArrayPool<WaiterNode>.Shared.Rent(waitersCount);
                waiters.CopyTo(waiterToNotify);
                waiters.Clear();
            }

            // Auto-reset after notifying waiters
            this.hasResult = false;
            this.error = null;
            this.result = default;
        }

        for (var i = 0; i < waitersCount; i++)
        {
            waiterToNotify[i].SetException(exception);
        }
        ArrayPool<WaiterNode>.Shared.Return(waiterToNotify);
    }
}