#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace ChibiRuby.Unity
{
    /// <summary>
    /// <see cref="MRubyFiberScheduler"/> driven by Unity's player loop via
    /// <see cref="Awaitable"/>. Parking uses
    /// <see cref="AwaitableCompletionSource{T}"/> (pool-backed, GC-friendly)
    /// instead of <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>,
    /// and sleep / yield hooks resume fibers on the main Unity thread on the
    /// player-loop tick boundary.
    /// </summary>
    public class UnityFiberScheduler : MRubyFiberScheduler
    {
        readonly ConcurrentDictionary<RFiber, AwaitableCompletionSource<MRubyValue>> parkedFibers = new();

        /// <summary>
        /// Park the current fiber on an <see cref="AwaitableCompletionSource{T}"/>.
        /// Settles through the overridden
        /// <see cref="SetResult"/> / <see cref="SetCancelled"/> / <see cref="SetException"/>.
        /// </summary>
        public override FiberContinuation Suspend()
        {
            var fiber = MRubyState.CurrentFiber;
            var entry = new AwaitableCompletionSource<MRubyValue>();
            if (!parkedFibers.TryAdd(fiber, entry))
            {
                ThrowAlreadyParked(fiber, "Suspend");
            }

            _ = WaitAndResumeAsync(fiber, entry);
            var continuation = new FiberContinuation(this, fiber);
            fiber.Yield();
            return continuation;

            static async Awaitable WaitAndResumeAsync(RFiber fiber, AwaitableCompletionSource<MRubyValue> entry)
            {
                // Force async boundary so the caller-side fiber.Yield() runs
                // before any resume could fire on the VM frame.
                await Awaitable.NextFrameAsync();

                MRubyValue value;
                try { value = await entry.Awaitable; }
                catch (OperationCanceledException) { TryResume(fiber, MRubyValue.Nil); return; }
                catch (Exception ex) { TryResumeWithException(fiber, ex); return; }
                TryResume(fiber, value);
            }
        }

        /// <summary>
        /// <c>Kernel#sleep</c>: park the fiber and resume after
        /// <paramref name="duration"/> elapses in player-loop time.
        /// Cancellation (user token or scheduler disposal) resumes the fiber with <c>nil</c>.
        /// </summary>
        public override void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            var continuation = Suspend();
            _ = SleepAsync(duration, cancellationToken, DisposalToken, continuation);

            static async Awaitable SleepAsync(TimeSpan duration, CancellationToken userCt, CancellationToken disposalCt, FiberContinuation cont)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCt, disposalCt);
                try
                {
                    await Awaitable.WaitForSecondsAsync((float)duration.TotalSeconds, linked.Token);
                    cont.Resume();
                }
                catch (OperationCanceledException ex)
                {
                    cont.SetCancelled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    cont.SetException(ex);
                }
            }
        }

        /// <summary>
        /// <c>Thread.pass</c>: park the fiber until the next Unity frame.
        /// </summary>
        public override void Yield(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var continuation = Suspend();
            _ = YieldAsync(cancellationToken, DisposalToken, continuation);

            static async Awaitable YieldAsync(CancellationToken userCt, CancellationToken disposalCt, FiberContinuation cont)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCt, disposalCt);
                try
                {
                    await Awaitable.NextFrameAsync(linked.Token);
                    cont.Resume();
                }
                catch (OperationCanceledException ex)
                {
                    cont.SetCancelled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    cont.SetException(ex);
                }
            }
        }

        protected override void SetResult(RFiber fiber, MRubyValue value)
        {
            if (parkedFibers.TryRemove(fiber, out var entry))
                entry.TrySetResult(value);
        }

        protected override void SetCancelled(RFiber fiber, CancellationToken cancellationToken)
        {
            // AwaitableCompletionSource has no token-preserving overload; the token
            // is discarded here but the cancel still resumes the fiber with nil.
            if (parkedFibers.TryRemove(fiber, out var entry))
                entry.TrySetCanceled();
        }

        protected override void SetException(RFiber fiber, Exception exception)
        {
            if (parkedFibers.TryRemove(fiber, out var entry))
                entry.TrySetException(exception);
        }

        public override void Dispose()
        {
            // Drain parked fibers directly. Base.Dispose() cancels its own disposeSource,
            // which propagates into linked tokens used by KernelSleep / Yield above and
            // cancels their in-flight Awaitable timers.
            foreach (var kv in parkedFibers) kv.Value.TrySetCanceled();
            parkedFibers.Clear();
            base.Dispose();
        }
    }
}
