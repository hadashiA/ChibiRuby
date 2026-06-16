using ChibiRuby.Compiler;

namespace ChibiRuby.Tests;

[TestFixture]
public class VmTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("__log"u8), (state, _) =>
        {
            var arg = state.GetArgumentAt(0);
            TestContext.Out.WriteLine(state.Stringify(arg).ToString());
            return MRubyValue.Nil;
        });
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
        mrb.Dispose();
    }

    [Test]
    public void Recursive()
    {
        var result = Exec("""
                          def fibonacci(n)
                            return n if n <= 1
                            fibonacci(n - 1) + fibonacci(n - 2)
                          end

                          fibonacci 10
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(55)));
    }

    [Test]
    public void StackOverflow()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Exec("""
                 def f(x)
                   f(x + 1)
                 end
                 f(1)
                 """u8);
        }, "stack level too deep");
    }

    [Test]
    public void Closure()
    {
         var result = Exec("""
                           def fb
                             n = 0
                             Proc.new do
                               n += 1
                               case
                               when n % 15 == 0
                               else n
                               end
                             end
                           end
                           fb.call
                           """u8);
         Assert.That(result, Is.EqualTo(new MRubyValue(1)));
    }

    [Test]
    public void CatchHandler()
    {
        var result = Exec("""
                          loops = 0
                          limit = 2
                          loop do
                            begin
                              limit -= 1
                              break unless limit > 0
                              raise "!"
                            rescue
                              redo
                            ensure
                              loops += 1
                            end
                          end
                          loops
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(2)));
    }

    [Test]
    public void ModuleInclude()
    {
        var result = Exec("""
                          module M
                            def foo
                              123
                            end
                          end

                          class A
                            include M
                          end

                          A.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }

    [Test]
    public void ClassNew()
    {
        var result = Exec("""
                          c = Class.new do
                            def foo
                              123
                            end
                          end
                          c.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }

    [Test]
    public void NewWithBlock()
    {
        var result = Exec("""
                          a = Array.new(1) { 123 }
                          a[0]
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }

    [Test]
    public void ReturnBlk()
    {
        var result = Exec("""
                          [1,2,3].find{|x| x == 2 }
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(2)));
    }

    [Test]
    public void DefineAttr()
    {
        var result = Exec("""
                          class Foo
                            attr_reader :a

                            def initialize(a)
                              @a = a
                            end
                          end
                          Foo.new(123).a
                          """u8);

        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }

    [Test]
    public void NoStrictProcCall()
    {
        var result = Exec("""
                          def iter
                            yield 1
                          end
                          iter do |a, b=2, c|
                            c
                          end
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.Nil));
    }

    [Test]
    public void InstanceEval()
    {
        var result = Exec("""
                          class A
                            attr_reader :x

                            def foo
                              @x = 123
                            end
                          end

                          a = A.new
                          a.instance_eval { foo }
                          a.x
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }

    [Test]
    public void ClassEval()
    {
        var result = Exec("""
                          class A
                          end

                          A.class_eval do
                            def foo = 123
                          end

                          A.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(123)));
    }


    [Test]
    public void IntegerOverflowAdd()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 9223372036854775807
                                  a += 8
                                  return a
                 """u8);
        }, "Integer overflow in add (RangeError)");

        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 9223372036854775807
                                  a += 8000
                                  return a
                 """u8);
        }, "Integer overflow in add (RangeError)");
    }

    [Test]
    public void IntegerOverflowSub()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = -9223372036854775806
                                  a -= 8
                                  return a
                 """u8);
        }, "Integer overflow in sub (RangeError)");

        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = -9223372036854775806
                                  a -= 8000
                                  return a
                 """u8);
        }, "Integer overflow in sub (RangeError)");
    }

    [Test]
    public void IntegerOverflowMul()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 10000000000
                                   a *= 10000000000
                                  return a
                 """u8);
        }, "Integer overflow in mul (RangeError)");
    }

    [Test]
    public void LargeInteger32()
    {
        var result = Exec("1000000000"u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(1000000000)));
    }

    [Test]
    public void NilExt()
    {
        var result = Exec("""
                          nil.to_i
                          """u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(0)));
    }

    [Test]
    public void LoadI16MultiByes()
    {
        var result = Exec("-256"u8);
        Assert.That(result, Is.EqualTo(new MRubyValue(-256)));
    }

    [Test]
    public void GetInstances()
    {
        Exec("""
             class A
               def self.foo = @@foo

               def self.foo=(x)
                 @@foo = x
               end
             end

             class B
               attr_accessor :bar
             end
             @b = B.new

             module M
               class C
                 def self.foo = 999
               end
             end
             """u8);

        // get class instance
        var classA = mrb.GetConst(mrb.Intern("A"u8), mrb.ObjectClass);

        // call class method
        mrb.Send(classA, mrb.Intern("foo="u8), new MRubyValue(123));
        var result1 = mrb.Send(classA, mrb.Intern("foo"u8));
        Assert.That(result1, Is.EqualTo(new MRubyValue(123)));

        // root instance variable
        var instanceB = mrb.GetInstanceVariable(mrb.TopSelf, mrb.Intern("@b"u8));
        mrb.Send(instanceB, mrb.Intern("bar="u8), new MRubyValue(123));

        var result2 = mrb.Send(instanceB, mrb.Intern("bar"u8));
        Assert.That(result2, Is.EqualTo(new MRubyValue(123)));

        // find class instance on the hierarchy
        var classC = mrb.Send(mrb.ObjectClass, mrb.Intern("const_get"u8), mrb.NewString("M::C"u8));
        Assert.That(classC.IsObject, Is.True);
    }

    [Test]
    public void GlobalVariables()
    {
        var name = mrb.Intern("$foo"u8);

        Assert.That(mrb.GlobalVariableDefined(name), Is.False);
        Assert.That(mrb.GetGlobalVariable(name), Is.EqualTo(MRubyValue.Nil));

        mrb.SetGlobalVariable(name, new MRubyValue(123));
        Assert.That(mrb.GlobalVariableDefined(name), Is.True);
        Assert.That(mrb.GetGlobalVariable(name), Is.EqualTo(new MRubyValue(123)));

        // visible from Ruby
        var fromRuby = Exec("$foo"u8);
        Assert.That(fromRuby, Is.EqualTo(new MRubyValue(123)));

        // writable from Ruby, observable from C#
        Exec("$foo = 'hello'"u8);
        var updated = mrb.GetGlobalVariable(name);
        Assert.That(updated.Object, Is.InstanceOf<RString>());
        Assert.That(updated.As<RString>().ToString(), Is.EqualTo("hello"));

        Assert.That(mrb.RemoveGlobalVariable(name, out var removed), Is.True);
        Assert.That(removed.As<RString>().ToString(), Is.EqualTo("hello"));
        Assert.That(mrb.GlobalVariableDefined(name), Is.False);
        Assert.That(mrb.RemoveGlobalVariable(name, out _), Is.False);
    }

    public void Send_FixedArityOverloads()
    {
        Exec("""
             class Joiner
               def join1(a) = a.to_s
               def join2(a, b) = "#{a},#{b}"
               def join3(a, b, c) = "#{a},#{b},#{c}"
               def join4(a, b, c, d) = "#{a},#{b},#{c},#{d}"
             end
             """u8);

        var joinerClass = mrb.GetConst(mrb.Intern("Joiner"u8), mrb.ObjectClass);
        var joiner = mrb.Send(joinerClass, mrb.Intern("new"u8));

        var r1 = mrb.Send(joiner, mrb.Intern("join1"u8), new MRubyValue(1));
        Assert.That(mrb.Stringify(r1).ToString(), Is.EqualTo("1"));

        var r2 = mrb.Send(joiner, mrb.Intern("join2"u8), new MRubyValue(1), new MRubyValue(2));
        Assert.That(mrb.Stringify(r2).ToString(), Is.EqualTo("1,2"));

        var r3 = mrb.Send(joiner, mrb.Intern("join3"u8), new MRubyValue(1), new MRubyValue(2), new MRubyValue(3));
        Assert.That(mrb.Stringify(r3).ToString(), Is.EqualTo("1,2,3"));

        var r4 = mrb.Send(joiner, mrb.Intern("join4"u8), new MRubyValue(1), new MRubyValue(2), new MRubyValue(3), new MRubyValue(4));
        Assert.That(mrb.Stringify(r4).ToString(), Is.EqualTo("1,2,3,4"));
    }

    [Test]
    public void Send_FixedArityWithBlock()
    {
        Exec("""
             class WithBlock
               def call0 = yield
               def call1(a) = yield(a)
               def call2(a, b) = yield(a, b)
               def call3(a, b, c) = yield(a, b, c)
               def call4(a, b, c, d) = yield(a, b, c, d)
             end
             """u8);

        var klass = mrb.GetConst(mrb.Intern("WithBlock"u8), mrb.ObjectClass);
        var receiver = mrb.Send(klass, mrb.Intern("new"u8));

        var collectBlock = compiler.LoadSourceCode("Proc.new { |*xs| xs.map { |x| x.to_s }.join(',') }"u8).As<RProc>();

        var r1 = mrb.Send(receiver, mrb.Intern("call1"u8), new MRubyValue(10), collectBlock);
        Assert.That(mrb.Stringify(r1).ToString(), Is.EqualTo("10"));

        var r2 = mrb.Send(receiver, mrb.Intern("call2"u8), new MRubyValue(10), new MRubyValue(20), collectBlock);
        Assert.That(mrb.Stringify(r2).ToString(), Is.EqualTo("10,20"));

        var r3 = mrb.Send(receiver, mrb.Intern("call3"u8), new MRubyValue(10), new MRubyValue(20), new MRubyValue(30), collectBlock);
        Assert.That(mrb.Stringify(r3).ToString(), Is.EqualTo("10,20,30"));

        var r4 = mrb.Send(receiver, mrb.Intern("call4"u8), new MRubyValue(10), new MRubyValue(20), new MRubyValue(30), new MRubyValue(40), collectBlock);
        Assert.That(mrb.Stringify(r4).ToString(), Is.EqualTo("10,20,30,40"));
    }

    [Test]
    public void Send_KeywordArgumentsOnly()
    {
        Exec("""
             class WithKwargs
               def show(verbose:, mode:) = "verbose=#{verbose} mode=#{mode}"
             end
             """u8);

        var klass = mrb.GetConst(mrb.Intern("WithKwargs"u8), mrb.ObjectClass);
        var receiver = mrb.Send(klass, mrb.Intern("new"u8));

        var kargs = new[]
        {
            new KeyValuePair<Symbol, MRubyValue>(mrb.Intern("verbose"u8), MRubyValue.True),
            new KeyValuePair<Symbol, MRubyValue>(mrb.Intern("mode"u8), mrb.NewString("fast"u8)),
        };

        var result = mrb.Send(receiver, mrb.Intern("show"u8), kargs);
        Assert.That(mrb.Stringify(result).ToString(), Is.EqualTo("verbose=true mode=fast"));
    }

    [Test]
    public void RubySend_DispatchesRubyMethodWithMultipleArguments()
    {
        var result = Exec("""
                          class SenderProbe
                            attr_reader :seen

                            def target(a, b)
                              @seen = [a, b]
                              123
                            end

                            def call_target
                              send(:target, true, false)
                            end
                          end

                          probe = SenderProbe.new
                          [probe.call_target, probe.seen]
                          """u8).As<RArray>();

        Assert.That(result[0], Is.EqualTo(new MRubyValue(123)));

        var seen = result[1].As<RArray>();
        Assert.That(seen[0], Is.EqualTo(MRubyValue.True));
        Assert.That(seen[1], Is.EqualTo(MRubyValue.False));
    }

    [Test]
    public void RubySend_DispatchesRubyMethodWithKeywordArguments()
    {
        var result = Exec("""
                          class SenderKeywordProbe
                            def target(a, b:, c:)
                              [a, b, c]
                            end

                            def call_target
                              send(:target, 1, b: 2, c: 3)
                            end
                          end

                          SenderKeywordProbe.new.call_target
                          """u8).As<RArray>();

        Assert.That(result[0], Is.EqualTo(new MRubyValue(1)));
        Assert.That(result[1], Is.EqualTo(new MRubyValue(2)));
        Assert.That(result[2], Is.EqualTo(new MRubyValue(3)));
    }

    [Test]
    public void RubySend_ResumesNestedSendToUnderscoreNamedMethod()
    {
        var result = Exec("""
                          class UnderscoreNestedSendProbe
                            attr_reader :events

                            def initialize
                              @events = []
                              @_a = 0
                              @data = 0
                              @_p_c = 0
                            end

                            def imm(_read, _write)
                              @events << :imm
                              @data = 1
                            end

                            def _adc
                              @events << :_adc
                              tmp = @_a + @data + @_p_c
                              @_p_v = ~(@_a ^ @data) & (@_a ^ tmp) & 0x80
                              @_a = tmp & 0xff
                              @_p_c = tmp[8]
                            end

                            def r_op(instr, mode)
                              send(mode, true, false)
                              send(instr)
                            end

                            def run
                              __send__(:r_op, :_adc, :imm)
                              @events << :post
                              [@events, @_a, @data, @_p_c]
                            end
                          end

                          UnderscoreNestedSendProbe.new.run
                          """u8).As<RArray>();

        var events = result[0].As<RArray>();
        Assert.That(events.Length, Is.EqualTo(3));
        Assert.That(events[0].SymbolValue, Is.EqualTo(mrb.Intern("imm"u8)));
        Assert.That(events[1].SymbolValue, Is.EqualTo(mrb.Intern("_adc"u8)));
        Assert.That(events[2].SymbolValue, Is.EqualTo(mrb.Intern("post"u8)));
        Assert.That(result[1], Is.EqualTo(new MRubyValue(1)));
        Assert.That(result[2], Is.EqualTo(new MRubyValue(1)));
        Assert.That(result[3], Is.EqualTo(new MRubyValue(0)));
    }

    [Test]
    public void ArgAry_ForwardsSuperArgumentsWithRestAndPost()
    {
        var result = Exec("""
                          class ArgAryBase
                            def count_rest(a, *rest, b)
                              rest.size
                            end
                          end

                          class ArgAryChild < ArgAryBase
                            def count_rest(a, *rest, b)
                              super
                            end
                          end

                          ArgAryChild.new.count_rest(1, 2, 3, 4)
                          """u8);

        Assert.That(result, Is.EqualTo(new MRubyValue(2)));
    }

    [Test]
    public void InstanceVariableGetSet()
    {
        var result = Exec("""
                          obj = Object.new
                          set = obj.instance_variable_set(:@x, 42)
                          get = obj.instance_variable_get(:@x)
                          removed = obj.remove_instance_variable(:@x)
                          set == 42 && get == 42 && removed == 42 && obj.instance_variable_get(:@x).nil?
                          """u8);

        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    [Test]
    public void BoundMethodCallAndHashKey()
    {
        var result = Exec("""
                          class BoundMethodTarget
                            def initialize
                              @value = 3
                            end

                            def add(a, b)
                              @value + a + b
                            end
                          end

                          obj = BoundMethodTarget.new
                          m = obj.method(:add)
                          h = { obj.method(:add) => 1 }
                          m.call(4, 5) == 12 &&
                            m[6, 7] == 16 &&
                            m.name == :add &&
                            m.receiver == obj &&
                            h[obj.method(:add)] == 1
                          """u8);

        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    [Test]
    public void ArrayRotateBangAndInclude()
    {
        var result = Exec("""
                          a = [1, 2, 3, 4]
                          a.rotate!
                          ok1 = a == [2, 3, 4, 1]
                          a.rotate!(2)
                          ok2 = a == [4, 1, 2, 3]
                          a.rotate!(-3)
                          ok3 = a == [1, 2, 3, 4]
                          ok1 && ok2 && ok3 && a.include?(4) && !a.member?(9)
                          """u8);

        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    MRubyValue Exec(ReadOnlySpan<byte> code)
    {
        using var compilation = compiler.Compile(code);
        return mrb.LoadBytecode(compilation.AsBytecode());
    }
}
