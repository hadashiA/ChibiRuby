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

namespace ChibiRuby.SourceGenerator;

internal sealed record RubyTypeInfo(
    string RubyName,
    string? Superclass,
    bool IsModule,
    string? TypeParameters,
    string CSharpTypeFullName,
    string? Summary,
    string? Example,
    EquatableArray<RubyMemberInfo> Members);

internal sealed record RubyMemberInfo(
    string FieldName,
    string? Signature,
    string? Summary,
    string? Example);

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
    EquatableArray<string> BodyLines,
    EquatableArray<string> HeaderComments);

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
    public const string RubyClassAttributeName = "ChibiRuby.RubyClassAttribute";
    public const string RubyModuleAttributeName = "ChibiRuby.RubyModuleAttribute";
    public const string RubyDefAttributeName = "ChibiRuby.RubyDefAttribute";

    public static void Register(IncrementalGeneratorInitializationContext context, IReadOnlyDictionary<string, string> knownSymbols)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select((p, _) =>
        {
            p.GlobalOptions.TryGetValue("build_property.ChibiRubyGenerator_RbsOutputDirectory", out var dir);
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

        // Extract class-level XML doc summary/example (same re-parse trick as for members).
        string? classSummary = null;
        string? classExample = null;
        {
            var doc = GetDocumentationCommentTriviaSyntax(cds);
            if (doc != null)
            {
                classSummary = GetSummary(doc);
                classExample = GetExample(doc);
            }
        }

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
            string? example = null;
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
                    summary ??= GetSummary(doc);
                    example ??= GetExample(doc);
                    if (!string.IsNullOrEmpty(summary) && !string.IsNullOrEmpty(example)) break;
                }
            }
            members.Add(new RubyMemberInfo(
                member.Name,
                sig,
                string.IsNullOrEmpty(summary) ? null : summary,
                string.IsNullOrEmpty(example) ? null : example));
        }

        return new RubyTypeInfo(
            rubyName, superclass, isModule, typeParameters, symbol.ToDisplayString(),
            string.IsNullOrEmpty(classSummary) ? null : classSummary,
            string.IsNullOrEmpty(classExample) ? null : classExample,
            new(members.ToImmutable()));
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

            // Both `Intern("...")` (inside MRubyState) and `mrb.Intern("...")`
            // (extension-method packages such as ChibiRuby.NIO).
            case InvocationExpressionSyntax invoke
                when InvocationNameOf(invoke) == "Intern"
                     && invoke.ArgumentList.Arguments.Count == 1
                     && invoke.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit:
                return lit.Token.ValueText;
        }
        return null;
    }

    static string? InvocationNameOf(InvocationExpressionSyntax invoke) => invoke.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null
    };

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

            // Class-level doc comment, emitted at the top level (no leading indent).
            if (type.Summary is { } classSum)
            {
                foreach (var line in classSum.Split('\n'))
                {
                    sb.AppendLine("# " + line.Trim());
                }
            }
            if (type.Example is { } classEx)
            {
                if (type.Summary is not null) sb.AppendLine("#");
                foreach (var line in classEx.Split('\n'))
                {
                    if (line.Length == 0) sb.AppendLine("#");
                    else sb.AppendLine("#   " + line);
                }
            }

            var typeParams = string.IsNullOrEmpty(type.TypeParameters) ? "" : $"[{type.TypeParameters}]";
            string header;
            if (type.IsModule)
                header = $"module {type.RubyName}{typeParams}";
            else if (string.IsNullOrEmpty(type.Superclass))
                header = $"class {type.RubyName}{typeParams}";
            else
                header = $"class {type.RubyName}{typeParams} < {type.Superclass}";
            sb.AppendLine(header);

            // Pre-process lib.rb blocks: hoist `include X` / `extend X` to the
            // top, dropping the include line and any immediately preceding
            // comment block from the body. This keeps the layout consistent
            // with C#-side `IncludeModule(...)` includes (which already emit
            // at the top).
            var libBlocks = blocksByName.TryGetValue(type.RubyName, out var rawBlocks) ? rawBlocks : null;
            var hoistedIncludes = new List<string>();
            var hoistedExtends = new List<string>();
            List<RbsBlock>? processedBlocks = null;
            if (libBlocks is not null)
            {
                processedBlocks = new List<RbsBlock>(libBlocks.Count);
                foreach (var b in libBlocks)
                {
                    var filtered = StripAndCollectIncludes(b.BodyLines, hoistedIncludes, hoistedExtends);
                    processedBlocks.Add(new RbsBlock(b.Name, b.IsModule, b.Superclass, new(filtered.ToImmutableArray()), b.HeaderComments));
                }
            }

            // include modules wired via Init.cs IncludeModule(klass, module),
            // plus anything hoisted from lib.rb. Dedup, preserve order.
            var includedModules = new List<string>();
            var seenIncludes = new HashSet<string>();
            foreach (var name in candidates.SelectMany(c => includesByClass[c]).Select(b => b.ModuleRubyName)
                         .Concat(hoistedIncludes))
            {
                if (seenIncludes.Add(name)) includedModules.Add(name);
            }
            foreach (var mod in includedModules)
            {
                sb.AppendLine($"  include {mod}");
            }
            foreach (var ext in hoistedExtends.Distinct())
            {
                sb.AppendLine($"  extend {ext}");
            }
            if (includedModules.Count > 0 || hoistedExtends.Count > 0) sb.AppendLine();

            EmitMembers(sb, type, relevant, isClassMethodSection: false);
            EmitMembers(sb, type, relevant, isClassMethodSection: true);

            // Append lib.rbs blocks for this class (with includes already stripped).
            if (processedBlocks is not null)
            {
                foreach (var b in processedBlocks)
                {
                    sb.AppendLine("  # --- from lib.rb ---");
                    foreach (var line in b.BodyLines) sb.AppendLine(line);
                }
            }

            sb.AppendLine("end");

            // For nested names like "HTTP::Session", sanitize "::" to "__" in
            // the file name so we don't emit colons into paths (illegal on
            // Windows, awkward elsewhere). The class header keeps the full
            // "HTTP::Session" name, which is valid RBS syntax.
            var fileName = ToSnake(type.RubyName.Replace("::", "__")) + ".rbs";
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
            sb.AppendLine("# Source: src/ChibiRuby/StdLib/lib.rb (via rbs prototype rb)");
            sb.AppendLine();
            // Emit captured pre-class comments (from lib.rbs) above the header.
            foreach (var hc in first.HeaderComments) sb.AppendLine(hc);
            sb.Append(first.IsModule ? "module " : "class ").Append(first.Name);
            if (!string.IsNullOrEmpty(first.Superclass)) sb.Append(" < ").Append(first.Superclass);
            sb.AppendLine();

            // Hoist `include X` / `extend X` to the top, before the rest of the body.
            var libIncludes = new List<string>();
            var libExtends = new List<string>();
            var filteredBlocks = kv.Value.Select(b =>
                new RbsBlock(b.Name, b.IsModule, b.Superclass,
                    new(StripAndCollectIncludes(b.BodyLines, libIncludes, libExtends).ToImmutableArray()),
                    b.HeaderComments)).ToList();
            foreach (var mod in libIncludes.Distinct()) sb.AppendLine($"  include {mod}");
            foreach (var ext in libExtends.Distinct()) sb.AppendLine($"  extend {ext}");
            if (libIncludes.Count > 0 || libExtends.Count > 0) sb.AppendLine();

            foreach (var b in filteredBlocks)
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
            if (member?.Example is { } ex)
            {
                // Blank `#` separator between summary and example (RDoc convention).
                if (member.Summary is not null) sb.AppendLine("  #");
                // Indent each example line by 2 spaces inside the comment so RDoc/yard
                // renders it as a code block.
                foreach (var line in ex.Split('\n'))
                {
                    if (line.Length == 0) sb.AppendLine("  #");
                    else sb.AppendLine("  #   " + line);
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

    static string? GetExample(DocumentationCommentTriviaSyntax docComment)
    {
        XmlElementSyntax? example = null;
        foreach (var n in docComment.Content)
        {
            if (n is XmlElementSyntax xe && xe.StartTag?.Name?.ToString() == "example")
            {
                example = xe;
                break;
            }
        }
        if (example == null) return null;

        // Unwrap a single <code>...</code> child if present.
        var contentList = example.Content;
        XmlElementSyntax? singleCode = null;
        foreach (var inner in contentList)
        {
            if (inner is XmlElementSyntax e && e.StartTag?.Name?.ToString() == "code")
            {
                if (singleCode != null) { singleCode = null; break; }
                singleCode = e;
            }
            else if (inner is XmlTextSyntax t)
            {
                // Allow surrounding whitespace-only text.
                foreach (var tok in t.TextTokens)
                {
                    if (!string.IsNullOrWhiteSpace(tok.ValueText)) { singleCode = null; goto done; }
                }
            }
        }
        done:
        if (singleCode != null) contentList = singleCode.Content;

        var text = RenderXmlPreservingLines(contentList);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    static string RenderXmlPreservingLines(SyntaxList<XmlNodeSyntax> content)
    {
        var sb = new StringBuilder();
        foreach (var n in content)
        {
            switch (n)
            {
                case XmlTextSyntax t:
                    foreach (var token in t.TextTokens)
                    {
                        sb.Append(token.ValueText);
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
                    sb.Append(RenderXmlPreservingLines(e.Content));
                    break;
                default:
                    sb.Append(n.ToString());
                    break;
            }
        }

        // Normalize line endings, drop leading/trailing blank lines,
        // and dedent by common indent.
        var raw = sb.ToString().Replace("\r\n", "\n");
        var lines = raw.Split('\n');

        // Safety net: when the XML parser falls back to source text (e.g. when an
        // example contains `<=>` and confuses the recovery path), raw `///` markers
        // and leftover closing tags can leak through `n.ToString()`. Strip leading
        // whitespace + `///` + optional space per line, then drop lines that are
        // only an XML closing tag like `</example>` or `</code>`.
        for (var k = 0; k < lines.Length; k++)
        {
            var l = lines[k];
            var p = 0;
            while (p < l.Length && (l[p] == ' ' || l[p] == '\t')) p++;
            if (p + 2 < l.Length && l[p] == '/' && l[p + 1] == '/' && l[p + 2] == '/')
            {
                p += 3;
                if (p < l.Length && l[p] == ' ') p++;
                l = l.Substring(p);
                lines[k] = l;
            }
            var trimmedLine = l.Trim();
            if (trimmedLine == "</example>" || trimmedLine == "</code>" ||
                trimmedLine == "<example>" || trimmedLine == "<code>")
            {
                lines[k] = "";
            }
        }
        var startIdx = 0;
        var endIdx = lines.Length - 1;
        while (startIdx <= endIdx && string.IsNullOrWhiteSpace(lines[startIdx])) startIdx++;
        while (endIdx >= startIdx && string.IsNullOrWhiteSpace(lines[endIdx])) endIdx--;
        if (startIdx > endIdx) return "";

        var common = int.MaxValue;
        for (var idx = startIdx; idx <= endIdx; idx++)
        {
            var l = lines[idx];
            if (string.IsNullOrWhiteSpace(l)) continue;
            var leading = 0;
            while (leading < l.Length && l[leading] == ' ') leading++;
            if (leading < common) common = leading;
        }
        if (common == int.MaxValue) common = 0;

        var result = new StringBuilder();
        for (var idx = startIdx; idx <= endIdx; idx++)
        {
            var l = lines[idx];
            if (string.IsNullOrWhiteSpace(l))
            {
                if (idx > startIdx) result.Append('\n');
                continue;
            }
            var stripped = common < l.Length ? l.Substring(common) : l;
            // Trim trailing whitespace per line.
            stripped = stripped.TrimEnd();
            if (idx > startIdx) result.Append('\n');
            result.Append(stripped);
        }
        return result.ToString();
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
        // Buffer of contiguous `#` comment lines at top level; flushed to a
        // class/module declaration when we encounter one.
        var pendingComments = new List<string>();
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            string? name = null;
            string? superclass = null;
            bool isModule = false;

            // Collect top-level (no indent) comment lines as a candidate header.
            if (line.Length > 0 && line[0] == '#')
            {
                pendingComments.Add(line);
                i++;
                continue;
            }
            // Blank line at top level: keep the buffer so multi-paragraph docs survive,
            // but a non-comment, non-class/module line resets it.
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

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

            if (name is null) { pendingComments.Clear(); i++; continue; }

            // Take the captured comments as this block's header.
            var headerComments = pendingComments.ToImmutableArray();
            pendingComments.Clear();

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

            blocks.Add(new RbsBlock(name, isModule, superclass, new(bodyLines.ToImmutableArray()), new(headerComments)));
        }
        return new(blocks.ToImmutable());
    }

    static IEnumerable<string> ClassExprCandidates(string rubyName, bool isModule)
    {
        var suffix = isModule ? "Module" : "Class";
        static string LowerFirst(string s) => s.Length > 0 ? char.ToLowerInvariant(s[0]) + s.Substring(1) : s;
        // For acronyms like "HTTP" — lower-camelCase becomes "http", not "hTTP".
        // Mirrors the heuristic devs actually use when naming local vars.
        static string LowerAcronymPrefix(string s)
        {
            if (s.Length == 0) return s;
            // Find the first run of consecutive uppercase letters at the start.
            var i = 0;
            while (i < s.Length && char.IsUpper(s[i])) i++;
            if (i <= 1) return LowerFirst(s);
            // If we ended in the middle of a word (next is lowercase), keep
            // the last uppercase letter as the start of that word.
            var prefix = i < s.Length && char.IsLower(s[i]) ? i - 1 : i;
            if (prefix == 0) return s;
            return s.Substring(0, prefix).ToLowerInvariant() + s.Substring(prefix);
        }

        yield return rubyName;
        yield return rubyName + suffix;
        yield return LowerFirst(rubyName) + suffix;
        // Acronym-aware variant for names like "HTTP" → variable "httpModule".
        var acronymed = LowerAcronymPrefix(rubyName);
        if (acronymed != rubyName && acronymed != LowerFirst(rubyName))
        {
            yield return acronymed + suffix;
        }

        // For nested names like "HTTP::Session", the runtime variable is
        // typically named after the leaf ("sessionClass"), not the FQN.
        // Yield the leaf-derived candidates so DefineMethod(sessionClass, …)
        // bindings still resolve to this type.
        var lastSep = rubyName.LastIndexOf("::", StringComparison.Ordinal);
        if (lastSep >= 0)
        {
            var leaf = rubyName.Substring(lastSep + 2);
            yield return leaf;
            yield return leaf + suffix;
            yield return LowerFirst(leaf) + suffix;
        }
    }

    static string PickPrimaryName(List<string> names)
    {
        foreach (var n in names)
        {
            if (n.Length > 0 && (char.IsLetter(n[0]) || n[0] == '_')) return n;
        }
        return names[0];
    }

    /// <summary>
    /// Walks lib.rb-derived body lines and pulls out `include X` / `extend X`
    /// declarations (with their immediately preceding contiguous `#` comment
    /// block, since those comments belong to the directive). Adds module names
    /// into <paramref name="includes"/> / <paramref name="extends"/> and returns
    /// the remaining lines.
    /// </summary>
    static List<string> StripAndCollectIncludes(EquatableArray<string> bodyLines, List<string> includes, List<string> extends)
    {
        var result = new List<string>(bodyLines.Length);
        foreach (var line in bodyLines)
        {
            var trimmed = line.TrimStart();
            string? name = null;
            bool isExtend = false;
            if (trimmed.StartsWith("include "))
            {
                name = trimmed.Substring(8).Trim();
            }
            else if (trimmed.StartsWith("extend "))
            {
                name = trimmed.Substring(7).Trim();
                isExtend = true;
            }
            if (name is not null && IsBareModuleRef(name))
            {
                if (isExtend) extends.Add(name);
                else includes.Add(name);
                // Drop preceding contiguous `#` comments + any single blank line.
                while (result.Count > 0)
                {
                    var prev = result[result.Count - 1].TrimStart();
                    if (prev.StartsWith("#") || prev.Length == 0)
                    {
                        result.RemoveAt(result.Count - 1);
                        if (prev.Length == 0) break;
                    }
                    else break;
                }
                continue;
            }
            result.Add(line);
        }
        return result;
    }

    /// <summary>True for an unqualified module-name reference like <c>Enumerable</c> or <c>Foo::Bar</c>.</summary>
    static bool IsBareModuleRef(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == ':')) return false;
        }
        return char.IsUpper(s[0]);
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
