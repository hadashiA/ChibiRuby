using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ChibiRuby;

/// <summary>
/// Handle returned by <see cref="MRubyFiberScheduler.Suspend"/>. Holds a
/// reference to its <see cref="MRubyFiberScheduler"/> and dispatches settle
/// operations through it. Idempotency is delegated to the underlying
/// parking entry (one-shot).
/// </summary>
public readonly struct FiberContinuation(MRubyFiberScheduler scheduler, RFiber fiber)
{
    /// <summary>The fiber this continuation will resume.</summary>
    public RFiber Fiber { get; } = fiber;

    /// <summary>
    /// Resume the suspended fiber with <paramref name="value"/>.
    /// </summary>
    public void Resume(MRubyValue value = default) => scheduler.SetResult(Fiber, value);

    /// <summary>
    /// Resume the fiber as if cancelled. Delivers <c>nil</c> as the resume
    /// value (matching CRuby fiber-scheduler cancellation). Pass the
    /// originating <paramref name="cancellationToken"/> to preserve it in
    /// the underlying <see cref="OperationCanceledException"/>.
    /// </summary>
    public void SetCancelled(CancellationToken cancellationToken = default)
        => scheduler.SetCancelled(Fiber, cancellationToken);

    /// <summary>
    /// Resume the fiber with an exception. The scheduler wraps
    /// <paramref name="exception"/> as an <see cref="RException"/> and sets
    /// it on <see cref="RFiber.PendingException"/>, so on resume the VM
    /// re-raises it (catchable by Ruby <c>rescue</c>).
    /// </summary>
    public void SetException(Exception exception)
    {
        if (exception is null) throw new ArgumentNullException(nameof(exception));
        scheduler.SetException(Fiber, exception);
    }
}

/// <summary>
/// Cooperative scheduler that bridges Ruby fibers to C# async code. Single
/// concrete class — hosts may subclass to override <see cref="KernelSleep"/>
/// / <see cref="Yield"/> / <see cref="Suspend"/>. Thread routing of resumes
/// is determined by the ambient <see cref="SynchronizationContext"/> at the
/// time of the <c>await</c> (e.g. the player-loop context on Unity), not
/// by any per-scheduler dispatch.
///
/// <para>
/// Hooks operate on <see cref="MRubyState.CurrentFiber"/> (captured at hook
/// entry) and return immediately; the fiber is resumed later via the
/// <see cref="TaskCompletionSource{T}"/> machinery built into
/// <see cref="Suspend"/>.
/// </para>
/// </summary>
public class MRubyFiberScheduler : IDisposable
{
    protected MRubyState MRubyState = default!;
    readonly ConcurrentDictionary<RFiber, TaskCompletionSource<MRubyValue>> blockedFibers = new();
    readonly CancellationTokenSource disposeSource = new();

    public CancellationToken DisposalToken => disposeSource.Token;

    /// <summary>
    /// Bind this scheduler to the given <see cref="ChibiRuby.MRubyState"/>. Invoked
    /// once by <see cref="MRubyState.UseFiberScheduler"/>.
    /// </summary>
    public virtual void Attach(MRubyState mrb) => MRubyState = mrb;

    /// <summary>
    /// Park the current fiber, run <paramref name="body"/> from the caller's
    /// thread (sync prefix runs there), and resume the fiber with body's
    /// result once it completes. The body receives the bound
    /// <see cref="ChibiRuby.MRubyState"/>.
    ///
    /// <para>
    /// On <see cref="OperationCanceledException"/> from body, the fiber
    /// resumes with <c>nil</c> (CRuby fiber-scheduler convention). On any
    /// other exception, the fiber re-raises it as a Ruby exception
    /// (catchable by <c>begin/rescue</c>).
    /// </para>
    /// </summary>
    public void Await(Func<MRubyState, ValueTask<MRubyValue>> body)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));
        var continuation = Suspend();
        _ = AwaitAsync(body, continuation);
        return;

        async ValueTask AwaitAsync(
            Func<MRubyState, ValueTask<MRubyValue>> body,
            FiberContinuation continuation)
        {
            try
            {
                var value = await body(MRubyState);
                continuation.Resume(value);
            }
            catch (OperationCanceledException ex)
            {
                continuation.SetCancelled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                continuation.SetException(ex);
            }
        }
    }

    /// <summary>
    /// Allocation-free overload of <see cref="Await(Func{MRubyState, ValueTask{MRubyValue}})"/>.
    /// Pass the closed-over data as <paramref name="state"/> and a static lambda for
    /// <paramref name="body"/> to avoid the implicit closure allocation that a capturing
    /// lambda would incur — useful on hot paths or in Unity where GC pressure matters.
    /// </summary>
    public void Await<TState>(TState state, Func<MRubyState, TState, ValueTask<MRubyValue>> body)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));
        var continuation = Suspend();
        _ = AwaitAsync(state, body, continuation);
        return;

        async ValueTask AwaitAsync(
            TState state,
            Func<MRubyState, TState, ValueTask<MRubyValue>> body,
            FiberContinuation continuation)
        {
            try
            {
                var value = await body(MRubyState, state);
                continuation.Resume(value);
            }
            catch (OperationCanceledException ex)
            {
                continuation.SetCancelled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                continuation.SetException(ex);
            }
        }
    }

    /// <summary>
    /// Low-level park primitive. Use when the resume signal arrives from an
    /// arbitrary event source (callbacks, IObservable, custom event loops)
    /// that doesn't fit the <see cref="Await"/> single-task shape.
    ///
    /// <para>
    /// The fiber is yielded internally before this returns — no race window
    /// for "arrange-Resume-before-Suspend". Settlement on the returned
    /// continuation is one-shot.
    /// </para>
    /// </summary>
    public virtual FiberContinuation Suspend()
    {
        var fiber = MRubyState.CurrentFiber;
        var entry = new TaskCompletionSource<MRubyValue>();
        if (!blockedFibers.TryAdd(fiber, entry))
        {
            ThrowAlreadyParked(fiber, "Suspend");
        }

        _ = WaitAndResumeAsync();
        var continuation = new FiberContinuation(this, fiber);
        fiber.Yield();
        return continuation;

        async ValueTask WaitAndResumeAsync()
        {
            // Force async boundary so the caller-side fiber.Yield() runs
            // before any resume could fire on the VM frame. Dictionary
            // cleanup is done atomically with the settle inside Set*; we
            // don't need to remove here.
            await Task.Yield();

            MRubyValue value;
            try { value = await entry.Task; }
            catch (OperationCanceledException) { TryResume(fiber, MRubyValue.Nil); return; }
            catch (Exception ex) { TryResumeWithException(fiber, ex); return; }
            TryResume(fiber, value);
        }
    }

    /// <summary><c>Kernel#sleep</c>: resume after <paramref name="duration"/>.</summary>
    /// <remarks>
    /// Default implementation routes through <see cref="Await"/> + <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Cancellation resumes the fiber with <c>nil</c>.
    /// </remarks>
    public virtual void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        Await((duration, cancellationToken, disposalToken: DisposalToken), async (_, x) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                x.cancellationToken, x.disposalToken);
            await Task.Delay(x.duration, linked.Token);
            return MRubyValue.Nil;
        });
    }

    /// <summary><c>Thread.pass</c>: cooperative yield.</summary>
    /// <remarks>
    /// Default implementation routes through <see cref="Await"/> + <see cref="Task.Yield"/>.
    /// </remarks>
    public virtual void Yield(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return;
        Await(async _ =>
        {
            await Task.Yield();
            return MRubyValue.Nil;
        });
    }

    // ── FiberContinuation dispatch (called via the struct). ─────────────
    // First call wins: TryRemove is atomic so only one settler pulls the
    // entry. Removing from the dict BEFORE settling matters because the
    // TCS continuation runs synchronously inline with TrySet*; that
    // continuation hops into fiber.Resume, which may re-park the fiber
    // via another Suspend. The slot must already be free by then or
    // re-park hits "already parked".

    /// <summary>
    /// Dispatch a successful resume on <paramref name="fiber"/>. Default
    /// implementation routes through the built-in <see cref="TaskCompletionSource{T}"/>
    /// entry; subclasses that use an alternative completion mechanism
    /// (e.g. <c>AwaitableCompletionSource</c> on Unity) override this.
    /// </summary>
    protected internal virtual void SetResult(RFiber fiber, MRubyValue value)
    {
        if (blockedFibers.TryRemove(fiber, out var entry))
            entry.TrySetResult(value);
    }

    /// <summary>
    /// Dispatch a cancellation on <paramref name="fiber"/>. Resumes the fiber
    /// with <c>nil</c> (CRuby fiber-scheduler convention).
    /// </summary>
    protected internal virtual void SetCancelled(RFiber fiber, CancellationToken cancellationToken)
    {
        if (blockedFibers.TryRemove(fiber, out var entry))
            entry.TrySetCanceled(cancellationToken);
    }

    /// <summary>
    /// Dispatch an exception on <paramref name="fiber"/>. The exception is
    /// re-raised inside the fiber and is catchable by Ruby <c>rescue</c>.
    /// </summary>
    protected internal virtual void SetException(RFiber fiber, Exception exception)
    {
        if (blockedFibers.TryRemove(fiber, out var entry))
            entry.TrySetException(exception);
    }

    protected static void TryResume(RFiber fiber, MRubyValue value)
    {
        if (!fiber.IsAlive) return;
        // Exceptions are routed via resumeSource for WaitForTerminate observers;
        // swallow here so threadpool / context callbacks don't crash the process.
        try { fiber.Resume(value); }
        catch { }
    }

    protected static void TryResumeWithException(RFiber fiber, Exception ex)
    {
        if (!fiber.IsAlive) return;
        try
        {
            var mrb = fiber.MRubyState;
            var msg = ex.Message ?? ex.GetType().Name;
            fiber.PendingException = new RException(
                mrb.NewString(System.Text.Encoding.UTF8.GetBytes(msg)),
                mrb.GetExceptionClass(Names.RuntimeError));
            fiber.Resume();
        }
        catch { }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    protected static void ThrowAlreadyParked(RFiber fiber, string op) =>
        throw new InvalidOperationException(
            $"{op}: fiber is already parked under this scheduler. Each park must be matched by Resume/SetCancelled/SetException/cancel before another can be issued.");

    public virtual void Dispose()
    {
        disposeSource.Cancel();
        foreach (var kv in blockedFibers) kv.Value.TrySetCanceled();
        blockedFibers.Clear();
    }
}
