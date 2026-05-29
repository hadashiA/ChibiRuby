using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace ChibiRuby.Compiler.Editor
{
    [ScriptedImporter(1, "mrb")]
    public class MRubyByteCodeScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var source = File.ReadAllBytes(ctx.assetPath);
            var rbAsset = new TextAsset(source)
            {
                name = Path.GetFileName(ctx.assetPath)
            };
            ctx.AddObjectToAsset("Main", rbAsset);
            ctx.SetMainObject(rbAsset);
        }
    }
}
