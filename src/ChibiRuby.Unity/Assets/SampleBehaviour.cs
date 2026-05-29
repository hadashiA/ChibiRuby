using System;
using System.IO;
using System.Threading.Tasks;
using ChibiRuby;
using ChibiRuby.Compiler;
using ChibiRuby.Debugger.Dap;
using UnityEngine;

public class SampleBehaviour : MonoBehaviour
{
    void Start()
    {
        var mrb = MRubyState.Create(x =>
        {
            x.UseFiberScheduler();
        });

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("log"), (x, self) =>
        {
            var message = x.GetArgumentAsStringAt(0);
            Debug.Log(message.ToString());
            return MRubyValue.Nil;
        });

        var compiler = MRubyCompiler.Create(mrb);

        var dapServer = new MRubyDapServer(mrb, compiler, log: (logLevel, message, ex) =>
        {
            var line = ex is null ? message : $"{message}\n{ex}";
            switch (logLevel)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(line);
                    break;
                case LogLevel.Error or LogLevel.Critical:
                    Debug.LogError(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        });

        Task.Run(async () =>
        {
            await dapServer.StartAsync(destroyCancellationToken);
        });

        destroyCancellationToken.Register(dapServer.Dispose);

        // Pass the absolute disk path of the .rb file so the bytecode's DBG section
        // records a real filename — otherwise mruby's compiler defaults to "-e" (the
        // `mruby -e` CLI mode marker), which the DAP stackTrace response then surfaces
        // to VSCode as `Source.path = "-e"`, and VSCode tries to open a file called "-e".
        // In a real game build (no asset on disk) you'd want a synthetic but stable
        // path like "ruby/sample.rb" instead.
        var scriptPath = $"{Application.dataPath}/ruby/sample.rb";
        using var compilation = compiler.CompileFile(scriptPath);

        var fiber = mrb.ParseBytecodeAsFiber(compilation.AsBytecode());
        fiber.Resume(Array.Empty<MRubyValue>());
        // fiber.WaitForTerminateAsync(destroyCancellationToken);

        destroyCancellationToken.Register(mrb.Dispose);
    }
}
