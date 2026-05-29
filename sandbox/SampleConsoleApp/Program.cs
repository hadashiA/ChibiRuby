// See https://aka.ms/new-console-template for more information


using System.Buffers;
using System.Text;
using ChibiRuby;
using ChibiRuby.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using JitInspect;

MRubyState mRubyDState = MRubyState.Create();

var compiler = MRubyCompiler.Create(mRubyDState);

var arrayBufferWriter = new ArrayBufferWriter<byte>();

using var compilation = compiler.Compile(ReadBytes("test.rb"));
var fileIrep = compilation.ToIrep();

Dump(fileIrep);


Console.WriteLine(Encoding.UTF8.GetString(arrayBufferWriter.WrittenSpan));
File.WriteAllBytes(GetAbsolutePath("dump.txt"), arrayBufferWriter.WrittenSpan);

Console.WriteLine(mRubyDState.Execute(fileIrep));

for (int i = 0; i < 10000; i++)
{
    mRubyDState.Execute(fileIrep);
}

var savePath = GetAbsolutePath("history");
var thisDir = GetThisDirectoryName();
var newJIitPath = Path.Join(thisDir, $"jit_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt");
var lastJitPaths = Directory.GetFiles(thisDir).Where(x=>x.Contains("jit_"));
if (!Directory.Exists(savePath))
{
    Directory.CreateDirectory(savePath);
}
if (lastJitPaths.Any())
{
    Console.WriteLine("Last:" + File.ReadAllLines(lastJitPaths.First())[^1]);
    foreach (var jitPath in lastJitPaths)
    {
        var last = jitPath;
        var dest = Path.Join(savePath, Path.GetFileName(jitPath));
        File.Move(last, dest);
    }

}
var method = typeof(MRubyState).GetMethod("Execute", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Irep), typeof(int), typeof(int)])!;
using var disassembler = JitDisassembler.Create();
var nextJitText = disassembler.Disassemble(method);
File.WriteAllText(newJIitPath, nextJitText);
Console.WriteLine("New:" + nextJitText.Split("\n")[^1]);

void Dump(Irep irep)
{
    mRubyDState.CodeDump(irep, arrayBufferWriter);
    foreach (var child in irep.Children)
    {
        Dump(child);
    }
}

static string GetThisDirectoryName([CallerFilePath] string callerFilePath = "")
{
    return Path.GetDirectoryName(callerFilePath)!;
}

static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
{
    return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
}

byte[] ReadBytes(string fileName)
{
    var path = GetAbsolutePath(Path.Join("ruby", fileName));
    return File.ReadAllBytes(path);
}