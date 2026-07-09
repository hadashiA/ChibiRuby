using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ChibiRuby.Debugger.Dap.Protocol;

namespace ChibiRuby.Debugger.Dap.Tests;

[TestFixture]
public class ChibiRubyDapServerTest
{
    string scriptPath = default!;

    [SetUp]
    public void BeforeEach()
    {
        scriptPath = Path.Combine(Path.GetTempPath(), $"mruby-debug-{System.Guid.NewGuid():N}.rb");
    }

    [TearDown]
    public void AfterEach()
    {
        if (File.Exists(scriptPath)) File.Delete(scriptPath);
    }

    [Test]
    public async Task Initialize_RespondsWithCapabilitiesAndInitializedEvent()
    {
        using var harness = new TestHarness();

        var response = await harness.InitializeAsync();
        Assert.That(response.Success, Is.True);
        Assert.That(response.Body!.SupportsConfigurationDoneRequest, Is.EqualTo(true));

        var initialized = await harness.WaitForEventAsync("initialized");
        Assert.That(initialized, Is.Not.Null);
    }

    [Test]
    public async Task Launch_RunsScript_StopsAtBindingBreak_AndContinues()
    {
        File.WriteAllText(scriptPath, "binding.break\n");

        using var harness = new TestHarness();
        await harness.InitializeAsync();
        await harness.WaitForEventAsync("initialized");
        await harness.ConfigurationDoneAsync();

        var launchResp = await harness.LaunchAsync(scriptPath);
        Assert.That(launchResp.Success, Is.True);

        var stopped = await harness.WaitForEventAsync<StoppedEvent>("stopped");
        Assert.That(stopped.Body.ThreadId, Is.EqualTo(1));
        Assert.That(stopped.Body.Reason, Is.EqualTo("pause"));

        var contResp = await harness.ContinueAsync(threadId: 1);
        Assert.That(contResp.Success, Is.True);

        await harness.WaitForEventAsync("terminated");
    }

    [Test]
    public async Task Evaluate_RunsRubyExpressionInBinding()
    {
        File.WriteAllText(scriptPath, "x = 7\nbinding.break\n");

        using var harness = new TestHarness();
        await harness.InitializeAsync();
        await harness.WaitForEventAsync("initialized");
        await harness.LaunchAsync(scriptPath);
        await harness.WaitForEventAsync("stopped");

        var evalResp = await harness.EvaluateAsync("1 + 2");
        Assert.That(evalResp.Success, Is.True);
        Assert.That(evalResp.Body.Result, Is.EqualTo("3"));

        await harness.ContinueAsync(threadId: 1);
        await harness.WaitForEventAsync("terminated");
    }

    [Test]
    public async Task Variables_ListsLocalsAndSelf()
    {
        File.WriteAllText(scriptPath, "a = 10\nb = 'hi'\nbinding.break\n");

        using var harness = new TestHarness();
        await harness.InitializeAsync();
        await harness.WaitForEventAsync("initialized");
        await harness.LaunchAsync(scriptPath);
        await harness.WaitForEventAsync("stopped");

        var stackResp = await harness.StackTraceAsync(threadId: 1);
        var frames = stackResp.Body.StackFrames;
        Assert.That(frames.Length, Is.GreaterThan(0));

        var scopesResp = await harness.ScopesAsync(frames[0].Id);
        var localsRef = scopesResp.Body.Scopes[0].VariablesReference;

        var varsResp = await harness.VariablesAsync(localsRef);
        var names = new List<string>();
        foreach (var v in varsResp.Body.Variables) names.Add(v.Name);
        Assert.That(names, Does.Contain("self"));
        Assert.That(names, Does.Contain("a"));
        Assert.That(names, Does.Contain("b"));

        await harness.ContinueAsync(threadId: 1);
        await harness.WaitForEventAsync("terminated");
    }

    [Test]
    public async Task Evaluate_RubyRaiseReturnsErrorResponse_AndContinueStillWorks()
    {
        File.WriteAllText(scriptPath, "binding.break\n:ok\n");

        using var harness = new TestHarness();
        await harness.InitializeAsync();
        await harness.WaitForEventAsync("initialized");
        await harness.LaunchAsync(scriptPath);
        await harness.WaitForEventAsync("stopped");

        var evalResp = await harness.EvaluateAsync("raise 'boom'");
        Assert.That(evalResp.Success, Is.False);
        Assert.That(evalResp.Message, Does.Contain("boom"));

        // Subsequent eval must still work.
        var evalResp2 = await harness.EvaluateAsync("1 + 1");
        Assert.That(evalResp2.Success, Is.True);
        Assert.That(evalResp2.Body.Result, Is.EqualTo("2"));

        await harness.ContinueAsync(threadId: 1);
        await harness.WaitForEventAsync("terminated");
    }
}
