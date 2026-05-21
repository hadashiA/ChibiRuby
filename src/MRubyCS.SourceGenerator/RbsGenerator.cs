using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MRubyCS.SourceGenerator;

internal sealed record RubyTypeInfo(
    string RubyName,
    string? Superclass,
    bool IsModule,
    string? TypeParameters,
    string CSharpTypeFullName,
    EquatableArray<RubyMemberInfo> Members);

internal sealed record RubyMemberInfo(
    string FieldName,
    string? Signature,
    string? Summary);

internal sealed record MethodBinding(
    string ClassExpr,
    string MethodName,
    string ContainingTypeName,
    string FieldName,
    bool IsClassMethod);

internal sealed record IncludeBinding(
    string ClassExpr,
    string ModuleRubyName);

internal sealed record RbsBlock(
    string Name,
    bool IsModule,
    string? Superclass,
    EquatableArray<string> BodyLines);

internal sealed record RbsOptions(string? OutputDirectory, bool IsDesignTimeBuild)
{
    public bool Enabled => !IsDesignTimeBuild && !string.IsNullOrEmpty(OutputDirectory);
}

internal readonly record struct EquatableArray<T>(ImmutableArray<T> Array) where T : notnull
{
    public bool Equals(EquatableArray<T> other) => Array.SequenceEqual(other.Array);
    public override int GetHashCode()
    {
        var h = 17;
        foreach (var x in Array) h = unchecked(h * 31 + x.GetHashCode());
        return h;
    }
    public int Length => Array.Length;
    public T this[int i] => Array[i];
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Array).GetEnumerator();
}

internal static class RbsGenerator
{
    public const string RubyClassAttributeName = "MRubyCS.RubyClassAttribute";
    public const string RubyModuleAttributeName = "MRubyCS.RubyModuleAttribute";
    public const string RubyDefAttributeName = "MRubyCS.RubyDefAttribute";

    public static void Register(IncrementalGeneratorInitializationContext context, IReadOnlyDictionary<string, string> knownSymbols)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select((p, _) =>
        {
            p.GlobalOptions.TryGetValue("build_property.MRubyCSGenerator_RbsOutputDirectory", out var dir);
            var isDesignTime = p.GlobalOptions.TryGetValue("build_property.DesignTimeBuild", out var dt) && dt == "true";
            return new RbsOptions(dir, isDesignTime);
        });

        var rubyTypes = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (n, _) => IsCandidateClassDeclaration(n),
            transform: static (ctx, _) => TryExtractRubyType(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        var bindings = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (n, _) => IsDefineMethodInvocation(n),
            transform: (ctx, _) => TryExtractBinding(ctx, knownSymbols))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        var includes = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (n, _) => IsIncludeModuleInvocation(n),
            transform: static (ctx, _) => TryExtractInclude(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // Read pre-generated RBS files (e.g. lib.rbs from `rbs prototype rb lib.rb`)
        // and parse class/module blocks so they can be merged into emitted sig/*.rbs.
        var rbBlocks = context.AdditionalTextsProvider
            .Where(t => t.Path.EndsWith(".rbs", StringComparison.OrdinalIgnoreCase))
            .Select((t, ct) => ParseRbsBlocks(t.GetText(ct)?.ToString() ?? ""))
            .Collect();

        var combined = rubyTypes.Combine(bindings).Combine(includes).Combine(rbBlocks).Combine(options);

        context.RegisterSourceOutput(combined, (spc, tuple) =>
        {
            var ((((types, defineCalls), includeCalls), blocks), opts) = tuple;
            if (!opts.Enabled) return;
            Emit(spc, types, defineCalls, includeCalls, blocks.SelectMany(b => b.Array).ToList(), opts.OutputDirectory!);
        });
    }

    static bool IsCandidateClassDeclaration(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0;
    }

    static RubyTypeInfo? TryExtractRubyType(GeneratorSyntaxContext ctx)
    {
        var cds = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(cds);
        if (symbol is null) return null;

        string? rubyName = null;
        string? superclass = null;
        string? typeParameters = null;
        bool isModule = false;

        foreach (var attr in symbol.GetAttributes())
        {
            var name = attr.AttributeClass?.ToDisplayString();
            if (name == RubyClassAttributeName)
            {
                rubyName = attr.ConstructorArguments.FirstOrDefault().Value as string;
                superclass = "Object";
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "Superclass" && named.Value.Value is string s) superclass = s;
                    else if (named.Key == "TypeParameters" && named.Value.Value is string tp) typeParameters = tp;
                }
                isModule = false;
                break;
            }
            if (name == RubyModuleAttributeName)
            {
                rubyName = attr.ConstructorArguments.FirstOrDefault().Value as string;
                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "TypeParameters" && named.Value.Value is string tp) typeParameters = tp;
                }
                isModule = true;
                break;
            }
        }

        if (rubyName is null) return null;

        var members = ImmutableArray.CreateBuilder<RubyMemberInfo>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not (IFieldSymbol or IPropertySymbol or IMethodSymbol)) continue;
            // Skip non-static — `[RubyDef]` only makes sense on static members.
            if (member.IsStatic == false) continue;
            // For methods, exclude constructors/operators/property accessors.
            if (member is IMethodSymbol ms && ms.MethodKind != MethodKind.Ordinary) continue;

            var rubyMethod = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == RubyDefAttributeName);
            if (rubyMethod is null) continue;

            string? sig = null;
            if (rubyMethod.ConstructorArguments.Length > 0 && rubyMethod.ConstructorArguments[0].Value is string s0)
                sig = s0;
            foreach (var named in rubyMethod.NamedArguments)
            {
                if (named.Key == "Signature" && named.Value.Value is string ss) sig = ss;
            }

            // Pattern from Cysharp/ConsoleAppFramework: re-parse with DocumentationMode.Parse
            // because ISymbol.GetDocumentationCommentXml() and structured trivia are gated by
            // <GenerateDocumentationFile>true</> — which we cannot require of consumers.
            string? summary = null;
            foreach (var declRef in member.DeclaringSyntaxReferences)
            {
                var node = declRef.GetSyntax();
                SyntaxNode? owner = node;
                while (owner != null && owner is not FieldDeclarationSyntax && owner is not PropertyDeclarationSyntax && owner is not MethodDeclarationSyntax)
                    owner = owner.Parent;
                if (owner == null) continue;
                var doc = GetDocumentationCommentTriviaSyntax(owner);
                if (doc != null)
                {
                    summary = GetSummary(doc);
                    if (!string.IsNullOrEmpty(summary)) break;
                }
            }
            members.Add(new RubyMemberInfo(member.Name, sig, string.IsNullOrEmpty(summary) ? null : summary));
        }

        return new RubyTypeInfo(rubyName, superclass, isModule, typeParameters, symbol.ToDisplayString(), new(members.ToImmutable()));
    }

    static bool IsIncludeModuleInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        var name = inv.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null
        };
        return name == "IncludeModule";
    }

    static IncludeBinding? TryExtractInclude(GeneratorSyntaxContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        var args = inv.ArgumentList.Arguments;
        if (args.Count != 2) return null;
        var classExpr = ExtractClassExpr(args[0].Expression);
        if (classExpr is null) return null;
        var moduleName = ExtractRubyNameFromExpr(args[1].Expression);
        if (moduleName is null) return null;
        return new IncludeBinding(classExpr, moduleName);
    }

    /// <summary>
    /// Given an expression like `KernelModule`, `comparableModule.As<RClass>()`,
    /// `ObjectClass`, etc., return the Ruby-side name (e.g. "Kernel", "Comparable", "Object").
    /// </summary>
    static string? ExtractRubyNameFromExpr(ExpressionSyntax expr)
    {
        // Unwrap `.As<T>()` / `.foo()` wrappers, walking down to the receiver.
        while (expr is InvocationExpressionSyntax inv2 && inv2.Expression is MemberAccessExpressionSyntax ma2)
        {
            expr = ma2.Expression;
        }
        string? ident = expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null
        };
        if (ident == null) return null;
        if (ident.EndsWith("Class", StringComparison.Ordinal) && ident.Length > 5) ident = ident.Substring(0, ident.Length - 5);
        else if (ident.EndsWith("Module", StringComparison.Ordinal) && ident.Length > 6) ident = ident.Substring(0, ident.Length - 6);
        if (ident.Length > 0 && char.IsLower(ident[0])) ident = char.ToUpperInvariant(ident[0]) + ident.Substring(1);
        return ident;
    }

    static bool IsDefineMethodInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        var name = inv.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null
        };
        return name is "DefineMethod" or "DefineClassMethod";
    }

    static MethodBinding? TryExtractBinding(GeneratorSyntaxContext ctx, IReadOnlyDictionary<string, string> knownSymbols)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        var args = inv.ArgumentList.Arguments;
        if (args.Count != 3) return null;

        var name = inv.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
            _ => null
        };
        var isClassMethod = name == "DefineClassMethod";

        var classExpr = ExtractClassExpr(args[0].Expression);
        if (classExpr is null) return null;

        var methodName = ExtractMethodName(args[1].Expression, knownSymbols);
        if (methodName is null) return null;

        if (args[2].Expression is not MemberAccessExpressionSyntax fieldExpr) return null;
        if (fieldExpr.Expression is not IdentifierNameSyntax containing) return null;
        var fieldName = fieldExpr.Name.Identifier.ValueText;
        var containingName = containing.Identifier.ValueText;

        return new MethodBinding(classExpr, methodName, containingName, fieldName, isClassMethod);
    }

    static string? ExtractClassExpr(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null
    };

    static string? ExtractMethodName(ExpressionSyntax expr, IReadOnlyDictionary<string, string> knownSymbols)
    {
        switch (expr)
        {
            case MemberAccessExpressionSyntax ma
                when ma.Expression is IdentifierNameSyntax ns && ns.Identifier.ValueText == "Names":
                return knownSymbols.TryGetValue(ma.Name.Identifier.ValueText, out var sym) ? sym : null;

            case InvocationExpressionSyntax invoke
                when invoke.Expression is IdentifierNameSyntax fn && fn.Identifier.ValueText == "Intern"
                     && invoke.ArgumentList.Arguments.Count == 1
                     && invoke.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit:
                return lit.Token.ValueText;
        }
        return null;
    }

    static void Emit(SourceProductionContext spc, ImmutableArray<RubyTypeInfo> types, ImmutableArray<MethodBinding> bindings, ImmutableArray<IncludeBinding> includes, List<RbsBlock> rbBlocks, string outputDir)
    {
        try
        {
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
        }
        catch
        {
            return;
        }

        var bindingsByClass = bindings.ToLookup(b => b.ClassExpr);
        var includesByClass = includes.ToLookup(b => b.ClassExpr);
        var blocksByName = rbBlocks
            .GroupBy(b => b.Name)
            .ToDictionary(g => g.Key, g => g.ToList());
        var emittedNames = new HashSet<string>();

        foreach (var type in types)
        {
            var candidates = ClassExprCandidates(type.RubyName, type.IsModule);
            var typeShortName = type.CSharpTypeFullName.Substring(type.CSharpTypeFullName.LastIndexOf('.') + 1);

            var relevant = candidates.SelectMany(c => bindingsByClass[c])
                .Where(b => b.ContainingTypeName == typeShortName)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# <auto-generated />");
            sb.AppendLine($"# Source: {type.CSharpTypeFullName}");
            sb.AppendLine();

            var typeParams = string.IsNullOrEmpty(type.TypeParameters) ? "" : $"[{type.TypeParameters}]";
            string header;
            if (type.IsModule)
                header = $"module {type.RubyName}{typeParams}";
            else if (string.IsNullOrEmpty(type.Superclass))
                header = $"class {type.RubyName}{typeParams}";
            else
                header = $"class {type.RubyName}{typeParams} < {type.Superclass}";
            sb.AppendLine(header);

            // include modules wired via Init.cs IncludeModule(klass, module)
            var includedModules = candidates.SelectMany(c => includesByClass[c])
                .Select(b => b.ModuleRubyName)
                .Distinct()
                .ToList();
            foreach (var mod in includedModules)
            {
                sb.AppendLine($"  include {mod}");
            }
            if (includedModules.Count > 0) sb.AppendLine();

            EmitMembers(sb, type, relevant, isClassMethodSection: false);
            EmitMembers(sb, type, relevant, isClassMethodSection: true);

            // Append lib.rbs blocks for this class
            if (blocksByName.TryGetValue(type.RubyName, out var blocks))
            {
                foreach (var b in blocks)
                {
                    sb.AppendLine("  # --- from lib.rb ---");
                    foreach (var line in b.BodyLines) sb.AppendLine(line);
                }
            }

            sb.AppendLine("end");

            var fileName = ToSnake(type.RubyName) + ".rbs";
            emittedNames.Add(type.RubyName);
            var fullPath = Path.Combine(outputDir, fileName);
            try
            {
                File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // ignore
            }
        }

        // Emit lib.rb-only classes/modules (Comparable, Enumerator, etc.)
        foreach (var kv in blocksByName)
        {
            if (emittedNames.Contains(kv.Key)) continue;
            var first = kv.Value[0];
            var sb = new StringBuilder();
            sb.AppendLine("# <auto-generated />");
            sb.AppendLine("# Source: src/MRubyCS/StdLib/lib.rb (via rbs prototype rb)");
            sb.AppendLine();
            sb.Append(first.IsModule ? "module " : "class ").Append(first.Name);
            if (!string.IsNullOrEmpty(first.Superclass)) sb.Append(" < ").Append(first.Superclass);
            sb.AppendLine();
            foreach (var b in kv.Value)
            {
                foreach (var line in b.BodyLines) sb.AppendLine(line);
            }
            sb.AppendLine("end");
            var fileName = ToSnake(kv.Key) + ".rbs";
            try
            {
                File.WriteAllText(Path.Combine(outputDir, fileName), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    static void EmitMembers(StringBuilder sb, RubyTypeInfo type, List<MethodBinding> bindings, bool isClassMethodSection)
    {
        var filtered = bindings.Where(b => b.IsClassMethod == isClassMethodSection).ToList();

        // Dedup by method name: last binding wins (matches runtime override semantics in Init.cs)
        var byName = new Dictionary<string, MethodBinding>();
        var nameOrder = new List<string>();
        foreach (var b in filtered)
        {
            if (!byName.ContainsKey(b.MethodName)) nameOrder.Add(b.MethodName);
            byName[b.MethodName] = b;
        }

        // Invert: field -> ordered list of method names that resolve to this field
        var byField = new Dictionary<string, List<string>>();
        var fieldOrder = new List<string>();
        foreach (var name in nameOrder)
        {
            var b = byName[name];
            if (b.MethodName != name) continue; // overridden, skip
            if (!byField.TryGetValue(b.FieldName, out var names))
            {
                byField[b.FieldName] = names = new List<string>();
                fieldOrder.Add(b.FieldName);
            }
            names.Add(name);
        }

        foreach (var fieldName in fieldOrder)
        {
            var names = byField[fieldName];
            var member = type.Members.Array.FirstOrDefault(m => m.FieldName == fieldName);

            // Prefer non-operator alphanumeric name as the primary (Ruby idiom)
            var primary = PickPrimaryName(names);
            var prefix = isClassMethodSection ? "self." : "";
            var signature = ComposeSignature(member);

            if (member?.Summary is { } sum)
            {
                foreach (var line in sum.Split('\n'))
                {
                    sb.AppendLine("  # " + line.Trim());
                }
            }
            sb.AppendLine($"  def {prefix}{primary}: {signature}");
            foreach (var n in names)
            {
                if (n == primary) continue;
                sb.AppendLine($"  alias {prefix}{n} {prefix}{primary}");
            }
            sb.AppendLine();
        }
    }

    // Adapted from Cysharp/ConsoleAppFramework (RoslynExtensions.cs).
    // ISymbol.GetDocumentationCommentXml() and structured doc trivia are only populated
    // when DocumentationMode is Parse/Diagnostic. With <GenerateDocumentationFile>false</>
    // (the common case), DocumentationMode is None and we get nothing. Workaround:
    // re-parse the syntax with DocumentationMode.Parse so trivia becomes structured.
    // Background: https://github.com/dotnet/roslyn/issues/58210
    static DocumentationCommentTriviaSyntax? GetDocumentationCommentTriviaSyntax(SyntaxNode node)
    {
        if (node.SyntaxTree.Options.DocumentationMode == DocumentationMode.None)
        {
            var opts = node.SyntaxTree.Options.WithDocumentationMode(DocumentationMode.Parse);
            var newTree = CSharpSyntaxTree.ParseText(node.ToFullString(), (CSharpParseOptions)opts);
            node = newTree.GetRoot();
        }

        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is DocumentationCommentTriviaSyntax structure) return structure;
        }
        return null;
    }

    static string? GetSummary(DocumentationCommentTriviaSyntax docComment)
    {
        XmlElementSyntax? summary = null;
        foreach (var n in docComment.Content)
        {
            if (n is XmlElementSyntax xe && xe.StartTag?.Name?.ToString() == "summary")
            {
                summary = xe;
                break;
            }
        }
        if (summary == null) return null;

        // Render summary content: strip "///" prefixes and resolve <see cref="..."/> to short names.
        var text = RenderXmlContent(summary.Content);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    static string RenderXmlContent(SyntaxList<XmlNodeSyntax> content)
    {
        var sb = new StringBuilder();
        foreach (var n in content)
        {
            switch (n)
            {
                case XmlTextSyntax t:
                    foreach (var token in t.TextTokens)
                    {
                        var s = token.ValueText;
                        // Tokens may contain raw `///` or leading whitespace from line continuations.
                        sb.Append(s);
                    }
                    break;
                case XmlEmptyElementSyntax see when see.Name?.ToString() == "see":
                    var cref = see.Attributes.OfType<XmlCrefAttributeSyntax>().FirstOrDefault()?.Cref?.ToString();
                    if (!string.IsNullOrEmpty(cref))
                    {
                        var dot = cref!.LastIndexOf('.');
                        sb.Append(dot >= 0 ? cref.Substring(dot + 1) : cref);
                    }
                    break;
                case XmlElementSyntax e when e.StartTag?.Name?.ToString() is "c" or "i" or "b":
                    sb.Append(RenderXmlContent(e.Content));
                    break;
                default:
                    sb.Append(n.ToString());
                    break;
            }
        }
        // Collapse whitespace and trim.
        var collapsed = new StringBuilder(sb.Length);
        var prevSpace = true;
        foreach (var ch in sb.ToString())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace) { collapsed.Append(' '); prevSpace = true; }
            }
            else
            {
                collapsed.Append(ch);
                prevSpace = false;
            }
        }
        return collapsed.ToString().Trim();
    }

    static EquatableArray<RbsBlock> ParseRbsBlocks(string source)
    {
        var blocks = ImmutableArray.CreateBuilder<RbsBlock>();
        if (string.IsNullOrEmpty(source)) return new(blocks.ToImmutable());

        var lines = source.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            string? name = null;
            string? superclass = null;
            bool isModule = false;

            if (trimmed.StartsWith("class "))
            {
                isModule = false;
                var rest = trimmed.Substring(6).Trim();
                var lt = rest.IndexOf('<');
                if (lt >= 0)
                {
                    name = rest.Substring(0, lt).Trim();
                    superclass = rest.Substring(lt + 1).Trim();
                }
                else { name = rest; }
                // strip type params
                var lb = name.IndexOf('[');
                if (lb >= 0) name = name.Substring(0, lb).Trim();
            }
            else if (trimmed.StartsWith("module "))
            {
                isModule = true;
                name = trimmed.Substring(7).Trim();
                var lb = name.IndexOf('[');
                if (lb >= 0) name = name.Substring(0, lb).Trim();
            }

            if (name is null) { i++; continue; }

            // collect lines until matching `end` at the same indent depth
            var bodyLines = new List<string>();
            i++;
            var depth = 1;
            while (i < lines.Length)
            {
                var l = lines[i];
                var lt = l.TrimStart();
                if (lt.StartsWith("class ") || lt.StartsWith("module ") || lt.StartsWith("def ") || lt.StartsWith("if ") || lt.StartsWith("unless ") || lt.StartsWith("while ") || lt.StartsWith("until ") || lt.StartsWith("begin ") || lt == "begin")
                {
                    // these may open a new scope; but for our simple parser, only nested class/module/def/etc. with `end` matter
                    // We only need to count one level of nesting for `class/module` and bare `def` (single-line body uses `def name: ...` in rbs, no `end`).
                    if (lt.StartsWith("class ") || lt.StartsWith("module "))
                    {
                        depth++;
                    }
                }
                if (lt == "end" || lt.StartsWith("end "))
                {
                    depth--;
                    if (depth <= 0) { i++; break; }
                }
                bodyLines.Add(l);
                i++;
            }

            blocks.Add(new RbsBlock(name, isModule, superclass, new(bodyLines.ToImmutableArray())));
        }
        return new(blocks.ToImmutable());
    }

    static IEnumerable<string> ClassExprCandidates(string rubyName, bool isModule)
    {
        var suffix = isModule ? "Module" : "Class";
        var lower = rubyName.Length > 0 ? char.ToLowerInvariant(rubyName[0]) + rubyName.Substring(1) : rubyName;
        yield return rubyName;
        yield return rubyName + suffix;
        yield return lower + suffix;
    }

    static string PickPrimaryName(List<string> names)
    {
        foreach (var n in names)
        {
            if (n.Length > 0 && (char.IsLetter(n[0]) || n[0] == '_')) return n;
        }
        return names[0];
    }

    static string ComposeSignature(RubyMemberInfo? member)
    {
        if (member?.Signature is { } sig && !string.IsNullOrWhiteSpace(sig))
            return sig.Trim();
        return "(*untyped) -> untyped";
    }

    static string ToSnake(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = s[i - 1];
                var nextLower = i + 1 < s.Length && char.IsLower(s[i + 1]);
                if (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && nextLower))
                {
                    sb.Append('_');
                }
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
