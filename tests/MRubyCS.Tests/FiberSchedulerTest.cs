using System.Diagnostics;
using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class FiberSchedulerTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Kernel#sleep / Thread.pass
    // ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Sleep_InsideFiber_YieldsToScheduler()
    {
        mrb.UseFiberScheduler();

        var code = """
                   sleep 0.05
                   :done
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);

        var sw = Stopwatch.StartNew();
        fiber.Resume();
        await fiber.WaitForTerminateAsync();
        sw.Stop();

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.04));
    }

    [Test]
    public async Task Sleep_InsideFiber_MultipleSleeps()
    {
        mrb.UseFiberScheduler();

        var code = """
                   3.times { sleep 0.02 }
                   :done
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);

        fiber.Resume();
        await fiber.WaitForTerminateAsync();

        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public void Sleep_AtRoot_BlocksHostThread()
    {
        // sleep called at the root fiber falls back to Thread.Sleep
        // because there is no caller to yield to.
        var code = """
                   sleep 0.02
                   :done
                   """u8;

        var sw = Stopwatch.StartNew();
        var result = compiler.LoadSourceCode(code);
        sw.Stop();

        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("done"u8)));
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.015));
    }

    [Test]
    public void Sleep_AtRootEvenWithScheduler_StillSynchronous()
    {
        // The scheduler is installed but the call site is the root fiber.
        // Sleep must fall back to Thread.Sleep; the scheduler hook must
        // not be invoked.
        var spy = new SpyScheduler();
        mrb.UseFiberScheduler(spy);

        var code = """
                   sleep 0.02
                   :done
                   """u8;

        var sw = Stopwatch.StartNew();
        compiler.LoadSourceCode(code);
        sw.Stop();

        Assert.That(spy.SleepCallCount, Is.EqualTo(0));
        Assert.That(sw.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(0.015));
    }

    [Test]
    public async Task Sleep_WithSyncContextHost_RoutesThroughContext()
    {
        // A host-set SynchronizationContext on the VM thread routes
        // fiber.Resume calls back through SynchronizationContext.Post,
        // because awaits inside the scheduler capture it by default.
        using var ctx = new PumpedSyncContext();
        using var scheduler = new MRubyFiberScheduler();
        mrb.UseFiberScheduler(scheduler);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 0.02; :done"u8);

        // Real hosts have SynchronizationContext.Current == captured ctx
        // on the VM thread; mirror that here so scheduler-hook awaits
        // capture our pumped context.
        ctx.Run(() => fiber.Resume());

        await ctx.PumpUntilAsync(() => !fiber.IsAlive, TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(ctx.PostCount, Is.GreaterThan(0), "Post should have been invoked");
    }

    [Test]
    public async Task ThreadPass_YieldsToScheduler()
    {
        // Thread.pass must hit the scheduler's Yield hook and resume on
        // the next tick. With the default scheduler the resume hops through
        // Task.Yield, so a counter incremented before+after observes the
        // yield round-trip.
        mrb.UseFiberScheduler();

        var code = """
                   x = 0
                   Thread.pass
                   x = 1
                   x
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync();

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.IntegerValue, Is.EqualTo(1));
    }

    [Test]
    public async Task SleepZero_RoutesToYield()
    {
        // sleep 0 must NOT call KernelSleep — it routes to Yield to avoid
        // an unnecessary Timer / Task.Delay allocation.
        var spy = new TrackingScheduler();
        mrb.UseFiberScheduler(spy);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 0; :done"u8);
        fiber.Resume();
        await fiber.WaitForTerminateAsync();

        Assert.That(spy.KernelSleepCalls, Is.EqualTo(0));
        Assert.That(spy.YieldCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Dispose_CancelsInflightKernelSleep()
    {
        // Disposing the scheduler mid-sleep must cancel the underlying
        // Task.Delay (via DisposalToken), not just unpark the fiber.
        var sched = new ObservableSleepScheduler();
        mrb.UseFiberScheduler(sched);

        var fiber = compiler.LoadSourceCodeAsFiber("sleep 100; :done"u8);
        fiber.Resume();

        await sched.SleepStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sched.Dispose();

        // Both the fiber unpark and the Await-body catch block run on independent
        // tasks after Dispose; wait until BOTH have settled (or the deadline hits)
        // so we don't sample SleepCancelled while the catch is still in flight.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while ((fiber.IsAlive || !sched.SleepCancelled) && DateTime.UtcNow < deadline)
            await Task.Delay(5);

        Assert.That(fiber.IsAlive, Is.False, "fiber should resume after Dispose");
        Assert.That(sched.SleepCancelled, Is.True,
            "underlying Task.Delay should observe cancellation through DisposalToken");
    }

    [Test]
    public async Task KernelSleep_Cancellation_ResumesFiberWithNil()
    {
        // Regression: with the old per-scheduler timer-based KernelSleep,
        // cancellation left the fiber permanently parked. The new default
        // Await + Task.Delay implementation resumes the fiber with nil
        // (CRuby fiber-scheduler convention) when cancellation fires.
        var sched = new CancellableSleepScheduler();
        mrb.UseFiberScheduler(sched);

        var fiber = compiler.LoadSourceCodeAsFiber("""
            v = sleep 100
            v.nil? ? :was_nil : :was_not_nil
            """u8);
        fiber.Resume();
        sched.Cts.Cancel();

        var result = await fiber.WaitForTerminateAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("was_nil"u8)));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Await
    // ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Await_RunsBodyAndResumesFiberWithResult()
    {
        mrb.UseFiberScheduler();

        var helloSym = mrb.Intern("hello"u8);
        var capturedRuby = new TaskCompletionSource<MRubyValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_value"u8),
            new MRubyMethod((state, self) =>
            {
                state.FiberScheduler!.Await(async _ =>
                {
                    await Task.Delay(20);
                    return new MRubyValue(helloSym);
                });
                return MRubyValue.Nil;
            }));
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("record"u8),
            new MRubyMethod((state, self) =>
            {
                capturedRuby.TrySetResult(state.GetArgumentAt(0));
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("""
            v = await_value
            record(v)
            """u8);
        fiber.Resume();

        var captured = await capturedRuby.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(captured.IsSymbol, Is.True);
        Assert.That(captured.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public async Task Await_BodyException_IsCatchableByRubyRescue()
    {
        mrb.UseFiberScheduler();

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_boom"u8),
            new MRubyMethod((state, self) =>
            {
                state.FiberScheduler!.Await(async s =>
                {
                    await Task.Delay(1);
                    throw new InvalidOperationException("boom");
                });
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("""
            begin
              await_boom
              :no_raise
            rescue => e
              e.message
            end
            """u8);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String));
        Assert.That(result.As<RString>().AsSpan().SequenceEqual("boom"u8), Is.True);
    }

    [Test]
    public async Task Await_BodyCanceled_ResumesWithNil()
    {
        mrb.UseFiberScheduler();

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_cancel"u8),
            new MRubyMethod((state, self) =>
            {
                state.FiberScheduler!.Await(async _ =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
                    await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                    return MRubyValue.Nil;
                });
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("""
            v = await_cancel
            v.nil? ? :was_nil : :was_not_nil
            """u8);
        fiber.Resume();
        var result = await fiber.WaitForTerminateAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.SymbolValue, Is.EqualTo(mrb.Intern("was_nil"u8)));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Existing fiber semantics regression
    // ─────────────────────────────────────────────────────────────────────

    [Test]
    public void ExistingFiberSemantics_Unchanged()
    {
        // Regression: the scheduler hook is invisible to ordinary
        // Resume/Yield flow. This mirrors FiberTest.Resume.
        var code = """
                   Fiber.new do |x|
                     Fiber.yield(x * 2)
                     Fiber.yield(x * 3)
                   end
                   """u8;

        var fiber = compiler.LoadSourceCode(code).As<RFiber>();

        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(200));
        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(300));
        // After the second yield resumes, the block ends with that resumed
        // value as its last expression -> Fiber#resume returns it.
        Assert.That(fiber.Resume(100).IntegerValue, Is.EqualTo(100));
        Assert.That(fiber.IsAlive, Is.False);
    }

    [Test]
    public async Task Resume_From_OtherThread_Delivers_Value_To_Ruby()
    {
        // Calls Resume(:hello) from a non-VM thread. This isolates whether
        // the resume-value flow has thread-affinity issues.
        mrb.UseFiberScheduler();

        var helloSym = mrb.Intern("hello"u8);
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("yield_only"u8),
            new MRubyMethod((state, self) =>
            {
                state.CurrentFiber.Yield();
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("yield_only"u8);
        fiber.Resume(); // first run -> hits Yield
        var second = await Task.Run(() => fiber.Resume(new MRubyValue(helloSym)));
        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(second.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public void Resume_With_Argument_Delivers_Value_To_Ruby_Through_Fiber_Yield()
    {
        // Direct test of the VM-level resume-value contract for top-level
        // programs. Calls fiber.Yield from inside a C# method, then the
        // test thread directly Resume(value)'s the fiber. The value should
        // land in Ruby (the C# method's apparent return).
        mrb.UseFiberScheduler();

        RFiber fiberRef = null!;
        var helloSym = mrb.Intern("hello"u8);
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("yield_then_resume"u8),
            new MRubyMethod((state, self) =>
            {
                fiberRef = state.CurrentFiber;
                fiberRef.Yield();
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("yield_then_resume"u8);
        var first = fiber.Resume();
        var second = fiber.Resume(new MRubyValue(helloSym));
        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(second.SymbolValue, Is.EqualTo(helloSym));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Low-level Suspend / FiberContinuation
    // ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Suspend_ExternalResume_DeliversValueToRuby()
    {
        // End-to-end: scheduler.Suspend() + continuation.Resume(value)
        // delivers value into Ruby as the apparent return of the C# method
        // that invoked Suspend.
        mrb.UseFiberScheduler();

        var helloSym = mrb.Intern("hello"u8);
        var capturedRuby = new TaskCompletionSource<MRubyValue>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_value"u8),
            new MRubyMethod((state, self) =>
            {
                var scheduler = state.FiberScheduler!;
                var continuation = scheduler.Suspend();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(20);
                    continuation.Resume(new MRubyValue(helloSym));
                });
                return MRubyValue.Nil;
            }));
        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("record"u8),
            new MRubyMethod((state, self) =>
            {
                capturedRuby.TrySetResult(state.GetArgumentAt(0));
                return MRubyValue.Nil;
            }));

        var fiber = compiler.LoadSourceCodeAsFiber("""
            v = await_value
            record(v)
            """u8);
        fiber.Resume();

        var captured = await capturedRuby.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(captured.IsSymbol, Is.True);
        Assert.That(captured.SymbolValue, Is.EqualTo(helloSym));
    }

    [Test]
    public async Task Suspend_Cancelled_ResumesEvenWithoutUnblock()
    {
        // When the only settle path is cancellation, the fiber must resume
        // with nil (not stay permanently parked).
        mrb.UseFiberScheduler();

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_value"u8),
            new MRubyMethod((state, self) =>
            {
                var scheduler = state.FiberScheduler!;
                var continuation = scheduler.Suspend();
                var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
                cts.Token.Register(() => continuation.SetCancelled());
                return MRubyValue.Nil;
            }));

        var code = "await_value; :done"u8;
        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (fiber.IsAlive && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        Assert.That(fiber.IsAlive, Is.False, "fiber should resume after cancellation (not stay parked)");
    }

    [Test]
    public async Task PendingException_IsCatchableByRubyRescue()
    {
        // The scheduler delivers an exception by setting PendingException
        // before Resume. The fiber wraps the parking call in begin/rescue
        // and must observe a normal Ruby raise that the rescue clause
        // catches — not a fiber-killing unwind.
        mrb.UseFiberScheduler();

        var yielded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed = new TaskCompletionSource<MRubyValue>(TaskCreationOptions.RunContinuationsAsynchronously);

        mrb.DefineMethod(mrb.KernelModule, mrb.Intern("await_boom"u8),
            new MRubyMethod((state, self) =>
            {
                var fiber = state.CurrentFiber;
                _ = Task.Run(async () =>
                {
                    await yielded.Task;
                    try
                    {
                        fiber.PendingException = new RException(
                            state.NewString("boom"u8),
                            state.GetExceptionClass(Names.RuntimeError));
                        resumed.SetResult(fiber.Resume());
                    }
                    catch (Exception ex)
                    {
                        resumed.SetException(ex);
                    }
                });
                fiber.Yield();
                return MRubyValue.Nil;
            }));

        var code = """
                   begin
                     await_boom
                     :no_raise
                   rescue => e
                     e.message
                   end
                   """u8;

        var fiber = compiler.LoadSourceCodeAsFiber(code);
        fiber.Resume();
        yielded.SetResult();
        var result = await resumed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.That(fiber.IsAlive, Is.False);
        Assert.That(result.VType, Is.EqualTo(MRubyVType.String),
            $"expected rescue to catch and return e.message; got {result.VType}");
        Assert.That(result.As<RString>().AsSpan().SequenceEqual("boom"u8), Is.True);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Counts KernelSleep / Yield calls.</summary>
    sealed class TrackingScheduler : MRubyFiberScheduler
    {
        public int KernelSleepCalls;
        public int YieldCalls;
        public override void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            KernelSleepCalls++;
            base.KernelSleep(duration, cancellationToken);
        }
        public override void Yield(CancellationToken cancellationToken = default)
        {
            YieldCalls++;
            base.Yield(cancellationToken);
        }
    }

    /// <summary>Counts KernelSleep invocations only.</summary>
    sealed class SpyScheduler : MRubyFiberScheduler
    {
        public int SleepCallCount;
        public override void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            SleepCallCount++;
            base.KernelSleep(duration, cancellationToken);
        }
    }

    /// <summary>Routes KernelSleep through a host-controlled cancellation token.</summary>
    sealed class CancellableSleepScheduler : MRubyFiberScheduler
    {
        public readonly CancellationTokenSource Cts = new();
        public override void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
            => base.KernelSleep(duration, Cts.Token);
    }

    /// <summary>Observes whether the underlying Task.Delay sees cancellation via DisposalToken.</summary>
    sealed class ObservableSleepScheduler : MRubyFiberScheduler
    {
        public readonly TaskCompletionSource SleepStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SleepCancelled;

        public override void KernelSleep(TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Await((duration, cancellationToken, disposalToken: DisposalToken, scheduler: this),
                async (_, x) =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        x.cancellationToken, x.disposalToken);
                    x.scheduler.SleepStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(x.duration, linked.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        x.scheduler.SleepCancelled = true;
                        throw;
                    }
                    return MRubyValue.Nil;
                });
        }
    }

    /// <summary>Minimal pumped SyncContext for the SyncContext-host test.</summary>
    sealed class PumpedSyncContext : SynchronizationContext, IDisposable
    {
        readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback cb, object? state)> queue = new();
        public int PostCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref PostCount);
            queue.Add((d, state));
        }

        public void Run(Action body)
        {
            var prev = Current;
            SetSynchronizationContext(this);
            try { body(); }
            finally { SetSynchronizationContext(prev); }
        }

        public async Task PumpUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline)
            {
                if (queue.TryTake(out var item, 50))
                {
                    var prev = Current;
                    SetSynchronizationContext(this);
                    try { item.cb(item.state); }
                    finally { SetSynchronizationContext(prev); }
                }
                else
                {
                    await Task.Yield();
                }
            }
        }

        public void Dispose() => queue.Dispose();
    }
}
