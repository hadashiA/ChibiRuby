using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ChibiRuby;

/// <summary>
/// Lightweight method-dispatch counter for understanding where the VM spends its
/// dispatches (the sampler cannot break down the monolithic <c>Execute</c> loop on macOS).
/// All counting hooks are <see cref="ConditionalAttribute"/> on <c>DISPATCH_PROFILE</c>, so
/// when that constant is not defined the calls — and therefore all overhead — are removed by
/// the compiler. Build a profiling run with
/// <c>-p:DefineConstants=TRACE%3BDISPATCH_PROFILE</c>.
///
/// Usage from a harness: <see cref="MRubyState.ResetDispatchProfile"/> before the measured
/// work, then <see cref="MRubyState.DumpDispatchProfile"/> after.
/// </summary>
public static class DispatchProfiler
{
    // Bytecode-send opcode dispatches (OpCode.Send/SSend/...), resolved in SendInternal.
    static long sendRProc;
    static long sendCSharp;
    // Dispatches that flow through __send__ / send (MRubyState.SendMeta), the path
    // optcarrot's CPU core uses for every 6502 instruction (`__send__(*DISPATCH[op])`).
    static long sendMetaRProc;
    static long sendMetaCSharp;
    // method_missing fallbacks.
    static long methodMissing;
    // Method-resolution cache (MRubyState.TryFindMethod).
    static long cacheHit;
    static long cacheMiss;

    static readonly Dictionary<uint, long> perMethod = new();
    static readonly Dictionary<uint, long> apostPerMethod = new();

    [Conditional("DISPATCH_PROFILE")]
    public static void Apost(Symbol methodId)
    {
        apostPerMethod.TryGetValue(methodId.Value, out var n);
        apostPerMethod[methodId.Value] = n + 1;
    }

    [Conditional("DISPATCH_PROFILE")]
    public static void Send(MRubyMethodKind kind, Symbol methodId)
    {
        if (kind == MRubyMethodKind.RProc) sendRProc++;
        else sendCSharp++;
        Bump(methodId);
    }

    [Conditional("DISPATCH_PROFILE")]
    public static void SendMeta(MRubyMethodKind kind, Symbol methodId)
    {
        if (kind == MRubyMethodKind.RProc) sendMetaRProc++;
        else sendMetaCSharp++;
        Bump(methodId);
    }

    [Conditional("DISPATCH_PROFILE")]
    public static void MethodMissing() => methodMissing++;

    [Conditional("DISPATCH_PROFILE")]
    public static void CacheHit() => cacheHit++;

    [Conditional("DISPATCH_PROFILE")]
    public static void CacheMiss() => cacheMiss++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Bump(Symbol methodId)
    {
        perMethod.TryGetValue(methodId.Value, out var n);
        perMethod[methodId.Value] = n + 1;
    }

    [Conditional("DISPATCH_PROFILE")]
    public static void Reset()
    {
        sendRProc = sendCSharp = sendMetaRProc = sendMetaCSharp = 0;
        methodMissing = cacheHit = cacheMiss = 0;
        perMethod.Clear();
        apostPerMethod.Clear();
    }

    /// <summary>
    /// Render the collected dispatch breakdown. Returns a message noting it is disabled when
    /// the profiler was not compiled in (all counters zero).
    /// </summary>
    public static string Report(MRubyState state, int topN = 25)
    {
        var totalSend = sendRProc + sendCSharp;
        var totalMeta = sendMetaRProc + sendMetaCSharp;
        var total = totalSend + totalMeta;
        if (total == 0)
        {
            return "[dispatch] no dispatches recorded — build with " +
                   "-p:DefineConstants=TRACE%3BDISPATCH_PROFILE to enable.";
        }

        var cacheTotal = cacheHit + cacheMiss;
        var sb = new StringBuilder();
        sb.AppendLine("[dispatch] ---- method dispatch breakdown ----");
        sb.AppendLine($"[dispatch] total dispatches: {total:N0}");
        sb.AppendLine(
            $"[dispatch]   bytecode Send : {totalSend,14:N0} ({Pct(totalSend, total)})  " +
            $"RProc={sendRProc:N0} CSharp={sendCSharp:N0}");
        sb.AppendLine(
            $"[dispatch]   __send__/send: {totalMeta,14:N0} ({Pct(totalMeta, total)})  " +
            $"RProc={sendMetaRProc:N0} CSharp={sendMetaCSharp:N0}");
        sb.AppendLine($"[dispatch]   method_missing: {methodMissing:N0}");
        if (cacheTotal > 0)
        {
            sb.AppendLine(
                $"[dispatch] method cache: hit={cacheHit:N0} miss={cacheMiss:N0} " +
                $"(hit-rate {Pct(cacheHit, cacheTotal)})");
        }

        sb.AppendLine($"[dispatch] top {topN} methods by dispatch count:");
        var i = 0;
        foreach (var (sym, count) in perMethod.OrderByDescending(kv => kv.Value).Take(topN))
        {
            string name;
            try { name = state.NameOf(new Symbol(sym)).ToString(); }
            catch { name = $"#sym{sym}"; }
            sb.AppendLine($"[dispatch]   {++i,3}. {count,14:N0} ({Pct(count, total)})  {name}");
        }

        var apostTotal = apostPerMethod.Values.Sum();
        if (apostTotal > 0)
        {
            sb.AppendLine($"[dispatch] APost (*rest binding) total: {apostTotal:N0} — top methods:");
            var j = 0;
            foreach (var (sym, count) in apostPerMethod.OrderByDescending(kv => kv.Value).Take(15))
            {
                string name;
                try { name = state.NameOf(new Symbol(sym)).ToString(); }
                catch { name = $"#sym{sym}"; }
                sb.AppendLine($"[dispatch]   apost {++j,3}. {count,14:N0}  {name}");
            }
        }
        return sb.ToString();
    }

    static string Pct(long n, long d) => d == 0 ? "0.0%" : $"{n * 100.0 / d:F1}%";
}
