using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class SpecTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;
    string rubyDir = default!;
    string rubyTestDir = default!;

    [OneTimeSetUp]
    public void BeforeAll()
    {
        rubyDir = Path.Join(TestContext.CurrentContext.TestDirectory, "ruby");
        rubyTestDir = Path.Join(rubyDir, "test");
    }

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create(x =>
        {
            x.DefineRegexp();
        });

        compiler = MRubyCompiler.Create(mrb);

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("__report_result"u8), (state, _) =>
        {
            var title = state.GetArgumentAsStringAt(0).ToString();
            var iso = state.GetArgumentAsStringAt(1).ToString();
            var results = state.GetArgumentAt(2).As<RArray>().AsSpan();

            var index = 0;
            foreach (var result in results)
            {
                var rec = result.As<RArray>();
                var passed = rec[0].Truthy;
                var message = state.Stringify(rec[1]).ToString();

                if (!rec[2].IsNil)
                {
                    message += $"\n{state.Stringify(rec[2]).ToString()}";
                }

                var tag = passed ? "Passed" : "Failed";
                var prefix = string.IsNullOrEmpty(iso) ? "" : $"({iso}) ";
                TestContext.Out.WriteLine($"{prefix}{title} [{index}] {tag} {message}");
                Assert.That(passed, Is.True, $"{prefix}{title} [{index}] {message}");
                index++;
            }
            return MRubyValue.Nil;
        });

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("__log"u8), (state, _) =>
        {
            var arg = state.GetArgumentAt(0);
            TestContext.Out.WriteLine(state.Stringify(arg).ToString());
            return MRubyValue.Nil;
        });

        // same as `File.fnmatch?` - supports *, ?, [...], and {...}
        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("_str_match?"u8), (state, _) =>
        {
            var pattern = state.GetArgumentAsStringAt(0).ToString();
            var str = state.GetArgumentAsStringAt(1).ToString();

            return GlobMatch(pattern, str);
        });

        mrb.DefineConst(mrb.ObjectClass, mrb.Intern("FLOAT_TOLERANCE"u8), 1e-10);
        compiler.LoadSourceCodeFile(Path.Join(rubyDir, "assert.rb"));
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    [TestCase("bs_literal.rb")]
    [TestCase("bs_block.rb")]
    [TestCase("basicobject.rb")]
    [TestCase("object.rb")]
    [TestCase("nil.rb")]
    [TestCase("false.rb")]
    [TestCase("true.rb")]
    [TestCase("symbol.rb")]
    [TestCase("ensure.rb")]
    [TestCase("iterations.rb")]
    [TestCase("literals.rb")]
    [TestCase("unicode.rb")]
    [TestCase("syntax.rb")]
    [TestCase("lang.rb")]
    // typesystem
    [TestCase("superclass.rb")]
    [TestCase("class.rb")]
    [TestCase("module.rb")]
    [TestCase("methods.rb")]
    // lib
    [TestCase("integer.rb")]
    [TestCase("float.rb")]
    [TestCase("string.rb")]
    [TestCase("array.rb")]
    [TestCase("hash.rb")]
    [TestCase("range.rb")]
    [TestCase("fiber.rb")]
    [TestCase("enumerator.rb")]
    [TestCase("enum_ext.rb")]
    [TestCase("proc.rb")]
    // error
    [TestCase("exception.rb")]
    [TestCase("indexerror.rb")]
    [TestCase("typeerror.rb")]
    [TestCase("localjumperror.rb")]
    // [TestCase("namerror.rb")]
    [TestCase("time.rb")]
    [TestCase("random.rb")]
    [TestCase("regexp.rb")]
    public void RubyScript(string fileName)
    {
        Assert.Multiple(() =>
        {
            Exec(fileName);
        });
    }

    void Exec(string fileName)
    {
        using var compilation = compiler.CompileFile(Path.Join(rubyTestDir, fileName));
        if (compilation.HasError)
        {
            throw new MRubyCompileException(string.Join("\n", compilation.Diagnostics));
        }
        mrb.LoadBytecode(compilation.AsBytecode());
    }

    /// <summary>
    /// Glob pattern matching similar to File.fnmatch? with FNM_EXTGLOB
    /// Supports: * (any string), ? (any char), [...] (character class), {...} (brace expansion)
    /// </summary>
    static bool GlobMatch(string pattern, string str)
    {
        // Handle brace expansion first
        var braceStart = -1;
        var braceEnd = -1;
        var nest = 0;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                i++; // skip escaped char
            }
            else if (c == '{')
            {
                if (nest++ == 0) braceStart = i;
            }
            else if (c == '}' && braceStart >= 0)
            {
                if (--nest == 0)
                {
                    braceEnd = i;
                    break;
                }
            }
        }

        if (braceStart >= 0 && braceEnd > braceStart)
        {
            // Expand braces
            var prefix = pattern[..braceStart];
            var suffix = pattern[(braceEnd + 1)..];
            var alternatives = pattern[(braceStart + 1)..braceEnd];

            // Split by comma (respecting nested braces)
            var parts = new List<string>();
            var partStart = 0;
            nest = 0;
            for (var i = 0; i < alternatives.Length; i++)
            {
                var c = alternatives[i];
                if (c == '\\' && i + 1 < alternatives.Length)
                {
                    i++;
                }
                else if (c == '{')
                {
                    nest++;
                }
                else if (c == '}')
                {
                    nest--;
                }
                else if (c == ',' && nest == 0)
                {
                    parts.Add(alternatives[partStart..i]);
                    partStart = i + 1;
                }
            }
            parts.Add(alternatives[partStart..]);

            foreach (var part in parts)
            {
                if (GlobMatch(prefix + part + suffix, str))
                    return true;
            }
            return false;
        }

        // No brace expansion, do pattern matching
        return GlobMatchNoBrace(pattern, 0, str, 0);
    }

    static bool GlobMatchNoBrace(string pattern, int p, string str, int s)
    {
        int pLen = pattern.Length, sLen = str.Length;
        int pTmp = -1, sTmp = -1;

        while (true)
        {
            if (p == pLen) return s == sLen;

            var c = pattern[p];

            if (c == '*')
            {
                // Skip consecutive *
                while (p < pLen && pattern[p] == '*') p++;
                if (p == pLen) return true;
                if (s == sLen) return false;
                pTmp = p;
                sTmp = s;
                continue;
            }

            if (c == '?')
            {
                if (s == sLen) return false;
                p++;
                s++;
                continue;
            }

            if (c == '[')
            {
                if (s == sLen) return false;
                var bracketEnd = MatchBracket(pattern, p + 1, str[s]);
                if (bracketEnd >= 0)
                {
                    p = bracketEnd;
                    s++;
                    continue;
                }
                // Bracket didn't match, try backtracking
                if (pTmp >= 0 && sTmp >= 0)
                {
                    p = pTmp;
                    s = ++sTmp;
                    continue;
                }
                return false;
            }

            // Handle escape
            if (c == '\\' && p + 1 < pLen)
            {
                p++;
                c = pattern[p];
            }

            if (s == sLen)
            {
                return p == pLen;
            }

            if (p == pLen || c != str[s])
            {
                // Mismatch, try backtracking with *
                if (pTmp >= 0 && sTmp >= 0)
                {
                    p = pTmp;
                    s = ++sTmp;
                    continue;
                }
                return false;
            }

            p++;
            s++;
        }
    }

    /// <summary>
    /// Match a bracket expression [...]
    /// Returns the index after ']' if matched, -1 otherwise
    /// </summary>
    static int MatchBracket(string pattern, int p, char c)
    {
        var pLen = pattern.Length;
        var negated = false;
        var matched = false;

        if (p < pLen && pattern[p] == '^')
        {
            negated = true;
            p++;
        }
        else if (p < pLen && pattern[p] == '!')
        {
            negated = true;
            p++;
        }

        var first = true;
        while (p < pLen)
        {
            var ch = pattern[p];

            if (ch == ']' && !first)
            {
                return (matched != negated) ? p + 1 : -1;
            }

            first = false;

            if (ch == '\\' && p + 1 < pLen)
            {
                p++;
                ch = pattern[p];
            }

            // Check for range: a-z
            if (p + 2 < pLen && pattern[p + 1] == '-' && pattern[p + 2] != ']')
            {
                var rangeEnd = pattern[p + 2];
                if (rangeEnd == '\\' && p + 3 < pLen)
                {
                    rangeEnd = pattern[p + 3];
                    p++;
                }
                if (c >= ch && c <= rangeEnd)
                {
                    matched = true;
                }
                p += 3;
            }
            else
            {
                if (c == ch)
                {
                    matched = true;
                }
                p++;
            }
        }

        // Unclosed bracket
        return -1;
    }
}
