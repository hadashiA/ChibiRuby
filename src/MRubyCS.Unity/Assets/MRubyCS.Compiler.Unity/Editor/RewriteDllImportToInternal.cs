#if UNITY_IOS || UNITY_WEBGL
using System.IO;
using System.Linq;
using Mono.Cecil;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MRubyCS.Compiler.Editor
{
    // On iOS and WebGL, native plugins are statically linked into the host
    // binary (Xcode app / wasm module), so [DllImport("libmruby")] must
    // resolve via the "__Internal" pseudo-module. The runtime assembly ships
    // with "libmruby" as the literal P/Invoke target; rewrite it here once
    // Unity has assembled the player script DLLs.
    public sealed class RewriteDllImportToInternal : IPostBuildPlayerScriptDLLs
    {
        const string SourceModule = "libmruby";
        const string TargetModule = "__Internal";
        const string AssemblyFileName = "MRubyCS.Compiler.dll";

        public int callbackOrder => 0;

        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            var platform = report.summary.platform;
            if (platform != BuildTarget.iOS && platform != BuildTarget.WebGL) return;

            foreach (var file in report.files)
            {
                if (Path.GetFileName(file.path) != AssemblyFileName) continue;
                Rewrite(file.path);
            }
        }

        static void Rewrite(string dllPath)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(dllPath));

            using var assembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters
            {
                ReadWrite = true,
                AssemblyResolver = resolver,
            });

            var modified = false;
            foreach (var module in assembly.Modules)
            {
                foreach (var moduleRef in module.ModuleReferences.Where(r => r.Name == SourceModule))
                {
                    moduleRef.Name = TargetModule;
                    modified = true;
                }
            }

            if (modified)
            {
                assembly.Write();
            }
        }
    }
}
#endif
