using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace ChibiRuby.Compiler.Editor
{
    [ScriptedImporter(1, "rb")]
    public class RubyScriptedImporter : ScriptedImporter
    {
        static MRubyCompiler? compiler;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var source = File.ReadAllBytes(ctx.assetPath);
            var rbAsset = new TextAsset(source)
            {
                name = Path.GetFileName(ctx.assetPath)
            };
            ctx.AddObjectToAsset("Main", rbAsset);
            ctx.SetMainObject(rbAsset);

            if (compiler == null)
            {
                var state = MRubyState.Create();
                compiler = MRubyCompiler.Create(state);
            }

            using var compilationResult = compiler.Compile(source, filename: ctx.assetPath);
            foreach (var x in compilationResult.Diagnostics)
            {
                if (x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.GeneratorError)
                {
                    ctx.LogImportError($"[ChibiRuby.Compiler] {ctx.assetPath}: {x}", rbAsset);
                }
                else
                {
                    ctx.LogImportWarning($"[ChibiRuby.Compiler] {ctx.assetPath}: {x}", rbAsset);
                }
            }
            if (compilationResult.HasError)
            {
                return;
            }
            var mrbAsset = new TextAsset(compilationResult.AsSpan())
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath) + ".mrb"
            };
            ctx.AddObjectToAsset("Bytecode", mrbAsset);
        }
    }
}
