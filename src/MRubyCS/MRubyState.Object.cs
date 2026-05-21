using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MRubyCS.Internals;
using MRubyCS.StdLib;
using Utf8StringInterpolation;

namespace MRubyCS;

partial class MRubyState
{
    public RString NewString(int capacity) => new(capacity, StringClass);

    public RString NewString(ReadOnlySpan<byte> utf8) => new(utf8, StringClass);

    public RString NewString(ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        return NewString(format.GetBufferWriter().WrittenSpan);
    }

    public RString NewString(string value) => NewString($"{value}");

    public RString NewStringOwned(byte[] buffer) => RString.Owned(buffer, StringClass);

    public RString NewStringOwned(byte[] buffer, int offset, int length) => RString.Owned(buffer, offset, length, StringClass);

    public RArray NewArray(int capacity) => new(capacity, ArrayClass);

    public RArray NewArray(params ReadOnlySpan<MRubyValue> values) => new(values, ArrayClass);

    public RHash NewHash(int capacity = 4) => new(
        capacity,
        HashKeyEqualityComparer,
        ValueEqualityComparer,
        HashClass);

    /// <summary>
    /// Returns an integer MRubyValue. The only difference from new MRubyValue() is that the full 64-bit digit count is available.
    /// </summary>
    public MRubyValue NewIntegerFlex(long value)
    {
        if (value <= MRubyValue.FixnumMax &&
            value >= MRubyValue.FixnumMin)
        {
            return new MRubyValue(value);
        }
        return new RInteger(value, IntegerClass);
    }

    public Symbol AsSymbol(MRubyValue value)
    {
        if (value.IsSymbol) return value.SymbolValue;
        if (value.VType == MRubyVType.String) return Intern(value.As<RString>());
        Raise(Names.TypeError, $"{Stringify(value)} is not a symbol nor a string");
        return default; // not reached
    }

    public long AsInteger(MRubyValue value)
    {
        if (value.IsInteger) return value.IntegerValue;

        if (value.IsFloat)
        {
            var f = value.FloatValue;
            if (double.IsNaN(f) || double.IsInfinity(f))
            {
                Raise(Names.RangeError, $"float `{f}` out of range");
            }
            return FloatMembers.ToI(this, value).IntegerValue;
        }

        // TODO: more numeric types

        Raise(Names.TypeError, $"{Stringify(value)} cannot be converted to Integer");
        return default;
    }

    public double AsFloat(MRubyValue value)
    {
        if (value.IsNil)
        {
            Raise(Names.TypeError, "can't convert nil into Float"u8);
        }

        switch (value.VType)
        {
            case MRubyVType.Integer:
                return value.IntegerValue;
            case MRubyVType.Float:
                return value.FloatValue;
        }
        Raise(Names.TypeError, $"{Stringify(value)} cannot be converted to Float");
        return default;
    }

    [Obsolete("Use AsSymbol instead")]
    public Symbol ToSymbol(MRubyValue value) => AsSymbol(value);

    [Obsolete("Use AsInteger instead")]
    public long ToInteger(MRubyValue value) => AsInteger(value);

    [Obsolete("Use AsFloat instead")]
    public double ToFloat(MRubyValue value) => AsFloat(value);

    public RString NameOf(Symbol symbol)
    {
        var result = NewString(symbolTable.NameOf(symbol));
        result.MarkAsFrozen();
        return result;
    }

    internal ReadOnlySpan<byte> TypeNameOf(MRubyValue value)
    {
        if (value is { VType: MRubyVType.Object, Object: { } obj })
        {
            var c = obj.Class;
            if (c.InstanceVariables.TryGet(Names.ClassNameKey, out var className))
            {
                // no name (yet)
                if (className.IsSymbol)
                {
                    var path = ClassPath.Find(this, c);
                    if (path.Length <= 1)
                    {
                        var name = NameOf(className.SymbolValue);
                        c.InstanceVariables.Set(Names.ClassNameKey, name);
                        return name.AsSpan();
                    }
                    var pathName = path.ToRString(this);
                    c.InstanceVariables.Set(Names.ClassNameKey, pathName);
                    return pathName.AsSpan();
                }
                // already cached
                if (className.VType == MRubyVType.String)
                {
                    return className.As<RString>().AsSpan();
                }
            }
        }
        return Utf8String.Format($"{value.VType}");
    }

    public RString NameOf(RClass c)
    {
        if (c.InstanceVariables.TryGet(Names.ClassNameKey, out var className))
        {
            // no name (yet)
            if (className.IsSymbol)
            {
                var path = ClassPath.Find(this, c);
                var pathName = path.ToRString(this);
                c.InstanceVariables.Set(Names.ClassNameKey, pathName);
                return pathName.Dup();
            }
            // already cached
            if (className.VType == MRubyVType.String)
            {
                return className.As<RString>().Dup();
            }
        }
        // top level class or module
        var prefix = c.VType == MRubyVType.Module
            ? "Module"u8
            : "Class"u8;
        var h = RuntimeHelpers.GetHashCode(c);
        var instantName = NewString($"#<{prefix}:0x{h:x}>");
        c.InstanceVariables.Set(Names.ClassNameKey, instantName);
        return instantName;
    }

    public RClass ClassOf(MRubyValue value)
    {
        if (value.Object is { } o) return o.Class;
        if (value.IsNil) return NilClass;
        if (value.IsFalse) return FalseClass;
        if (value.IsTrue) return TrueClass;
        if (value.IsSymbol) return SymbolClass;
        if (value.IsInteger) return IntegerClass;
        if (value.IsFloat) return FloatClass;
        Raise(Names.RuntimeError, "no class found"u8);
        return default!;
    }

    public RString ClassNameOf(MRubyValue value)
    {
        return NameOf(ClassOf(value));
    }

    public bool InstanceOf(MRubyValue value, RClass c)
    {
        return c == ClassOf(value).GetRealClass();
    }

    public bool KindOf(MRubyValue value, RClass c)
    {
        var classOfValue = ClassOf(value);
        c = c.AsOrigin();
        while (classOfValue != null!)
        {
            if (classOfValue.Class == c || classOfValue.MethodTable == c.MethodTable)
            {
                return true;
            }
            classOfValue = classOfValue.Super;
        }
        return false;
    }

    public bool ValueEquals(MRubyValue a, MRubyValue b)
    {
        if (a == b) return true;
        // value mixing with integer and float
        if (a.IsInteger && b.IsFloat)
        {
            if ((double)a.IntegerValue == b.FloatValue) return true;
        }
        else if (a.IsFloat && b.IsInteger)
        {
            if (a.FloatValue == (float)b.IntegerValue) return true;
        }
        else
        {
            if (TryFindMethod(ClassOf(a), Names.OpEq, out var method, out _) &&
                method != BasicObjectMembers.OpEq)
            {
                var result = Send(a, Names.OpEq, b);
                return result.Truthy;
            }
        }
        return false;
    }

    public int ValueCompare(MRubyValue a, MRubyValue b)
    {
        switch (a.VType)
        {
            case MRubyVType.Integer:
            case MRubyVType.Float:
                return NumberCompare(a, b);
            case MRubyVType.String:
                if (b.Object is RString bStr)
                {
                    return a.As<RString>().CompareTo(bStr);
                }
                return -2;
            default:
                var result = Send(a, Names.OpCmp, b);
                if (result.IsInteger) return (int)result.IntegerValue;
                return -2;
        }
    }

    public RString Stringify(MRubyValue value)
    {
        switch (value.VType)
        {
            case MRubyVType.Float:
                return NewStringOwned(Utf8String.Format($"{value.FloatValue}"));
            case MRubyVType.String:
                return value.As<RString>();
            case MRubyVType.Symbol:
                return NameOf(value.SymbolValue);
            case MRubyVType.Integer:
                return StringifyInteger(value, 10);
            case MRubyVType.SClass:
            case MRubyVType.Class:
            case MRubyVType.Module:
                return StringifyModule(value.As<RClass>());
            default:
                return ConvertType(value, MRubyVType.String, Names.ToS)
                    .As<RString>();
        }
    }

    public RString StringifyAny(MRubyValue value)
    {
        var className = NameOf(ClassOf(value));
        var rawString = value.IsImmediate
            ? Utf8String.Format($"#<{className}>")
            : Utf8String.Format($"#<{className}:{value.Object!.GetHashCode()}>");
        return NewStringOwned(rawString);
    }

    public MRubyValue GetInstanceVariable(RObject obj, Symbol key)
    {
        if (obj is RClass c)
        {
            return c.ClassInstanceVariables.Get(key);
        }

        if (obj is { } o)
        {
            return o.InstanceVariables.Get(key);
        }
        return MRubyValue.Nil;
    }

    public void SetInstanceVariable(RObject obj, Symbol key, MRubyValue value)
    {
        EnsureNotFrozen(obj);
        if (obj is RClass c)
        {
            c.ClassInstanceVariables.Set(key, value);
        }
        else
        {
            obj.InstanceVariables.Set(key, value);
        }
    }

    public MRubyValue RemoveInstanceVariable(RObject obj, Symbol key)
    {
        EnsureNotFrozen(obj);
        if (obj.InstanceVariables.Remove(key, out var removedValue))
        {
            return removedValue;
        }
        return MRubyValue.Undef;
    }

    public MRubyValue CloneObject(MRubyValue obj)
    {
        if (obj.Object is { } src)
        {
            if (src.VType == MRubyVType.SClass)
            {
                Raise(Names.TypeError, "can't clone singleton class"u8);
            }
            // Clone class (with singleton class)
            var clone = src.Clone();
            if (clone.VType is MRubyVType.Class or MRubyVType.Module)
            {
                clone.InstanceVariables.Remove(Names.ClassNameKey, out _);
            }
            clone.Class = CloneSingletonClass(obj);
            if (src.IsFrozen)
            {
                clone.MarkAsFrozen();
            }

            var cloneValue = clone;
            if (TryFindMethod(clone.Class, Names.InitializeCopy, out var method, out _) &&
                method != KernelMembers.InitializeCopy)
            {
                Send(cloneValue, Names.InitializeCopy, obj);
            }
            return cloneValue;
        }
        return obj;
    }

    public MRubyValue DupObject(MRubyValue obj)
    {
        if (obj.Object is { } src)
        {
            if (src.VType == MRubyVType.SClass)
            {
                Raise(Names.TypeError, "can't clone singleton class"u8);
            }

            var clone = src.Clone();
            var cloneValue = clone;

            if (TryFindMethod(clone.Class, Names.InitializeCopy, out var method, out _) &&
                method != KernelMembers.InitializeCopy)
            {
                Send(cloneValue, Intern("initialize_copy"u8), obj);
            }
            return cloneValue;
        }
        return obj;
    }

    public RString Inspect(MRubyValue value)
    {
        var converted = Send(value, Names.Inspect);
        if (converted.Object is RString str)
        {
            return str;
        }
        return Stringify(converted);
    }

    public RString InspectObject(MRubyValue value)
    {
        if (value.Object is { } obj)
        {
            if (obj.InstanceVariables.Length > 0)
            {
                var s = NewString($"-<{NameOf(obj.Class)}:{obj.GetHashCode()} ");
                if (Context.IsRecursiveCalling(Names.Inspect, value))
                {
                    s.Concat(" ...>"u8);
                    return s;
                }

                var first = true;
                foreach (var (k, v) in obj.InstanceVariables)
                {
                    var inspectedValue = Stringify(Send(v, Names.Inspect));
                    s.Concat(NewString($"{NameOf(k)}={inspectedValue} "));
                    if (!first)
                    {
                        s.Concat(", "u8);
                    }
                    first = false;
                }
                return s;
            }
        }
        return StringifyAny(value);
    }

    internal MRubyValue SplatArray(MRubyValue value)
    {
        if (value.Object is RArray array)
        {
            return array.Dup();
        }

        var methodId = Names.ToA;
        if (!RespondTo(value, methodId))
        {
            return NewArray(value);
        }
        var convertedValue = Send(value, methodId);
        if (convertedValue.IsNil)
        {
            return NewArray(value);
        }
        EnsureValueType(convertedValue, MRubyVType.Array);
        return convertedValue.As<RArray>().Dup();
    }

    internal void SpliceArray(RArray array, int start, int length, MRubyValue args)
    {
        if (length < 0)
        {
            Raise(Names.IndexError, "negative length "u8);
        }

        if (start < 0)
        {
            start += array.Length;
            if (start < 0)
            {
                Raise(Names.IndexError, $"index {start} is out of array");
            }
        }

        var end = start + length;
        if (array.Length < length || array.Length < end)
        {
            length = array.Length - start;
            end = start + length;
        }

        ReadOnlySpan<MRubyValue> newValues;
        if (args.Object is RArray argsArray)
        {
            newValues = argsArray.AsSpan();
        }
        else if (args.IsUndef)
        {
            newValues = ReadOnlySpan<MRubyValue>.Empty;
        }
        else
        {
            ref readonly var first = ref args;
            newValues = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in first), 1);
        }

        var originalLength = array.Length;
        if (start >= originalLength)
        {
            length = start + newValues.Length;
            array.MakeModifiable(length, true);
            if (start > originalLength)
            {
                array.AsSpan(originalLength, start - originalLength).Clear();
            }
        }
        else
        {
            var newLength = array.Length + newValues.Length - length;
            array.MakeModifiable(newLength, true);
            if (length != newValues.Length)
            {
                array.AsSpan(end, originalLength - end)
                    .CopyTo(array.AsSpan(start + newValues.Length));
            }
        }
        if (newValues.Length > 0)
        {
            newValues.CopyTo(array.AsSpan(start));
        }
    }

    internal RProc NewProc(Irep irep, RClass? targetClass = null, RClass? procClass = null)
    {
        ref var callInfo = ref Context.CurrentCallInfo;

        targetClass ??= (callInfo.Proc?.Scope ?? callInfo.Scope).TargetClass;
        return new RProc(irep, 0, procClass ?? ProcClass)
        {
            Upper = callInfo.Proc,
            Scope = targetClass
        };
    }

    internal RProc NewClosure(Irep irep, RClass? procClass = null)
    {
        ref var callInfo = ref Context.CurrentCallInfo;
        var env = callInfo.Scope as REnv;

        if (env is null)
        {
            if (callInfo.Proc is { } upper)
            {
                var methodId = callInfo.MethodId;
                if (upper.Scope is REnv { Context: null } x)
                {
                    methodId = x.MethodId;
                }

                var stackSize = upper.Irep.RegisterVariableCount;
                env = new REnv
                {
                    Context = Context,
                    StackPointer = callInfo.StackPointer,
                    StackSize = stackSize,
                    MethodId = methodId,
                    BlockArgumentOffset = callInfo.BlockArgumentOffset,
                    TargetClass = callInfo.Scope.TargetClass,
                    Visibility = callInfo.Visibility,
                    VisibilityBreak = callInfo.VisibilityBreak
                };
                callInfo.Scope = env;
            }
        }
        return new RProc(irep, 0, procClass ?? ProcClass)
        {
            Upper = callInfo.Proc,
            Scope = env,
        };
    }

    internal MRubyValue GetProcSelf(RProc proc, out RClass targetClass)
    {
        if (proc.Scope is REnv env)
        {
            if (env.Stack.Length <= 0)
            {
                Raise(Names.ArgumentError, "self is lost (probably ran out of memory when the block became independent)"u8);
            }
            targetClass = env.TargetClass;
            return env.Stack[0];
        }

        targetClass = ObjectClass;
        return TopSelf;
    }

    internal unsafe RString StringifyInteger(MRubyValue value, int bases)
    {
        if (bases is < 2 or > 36)
        {
            Raise(Names.ArgumentError, Utf8String.Format($"invalid radix {bases}"));
        }
        var n = value.IntegerValue;
        if (n is 0)
        {
            return NewString("0"u8);
        }

        ReadOnlySpan<byte> digitMap = "0123456789abcdefghijklmnopqrstuvwxyz"u8;
        var buf = stackalloc byte[65];
        var last = buf + 64;
        var b = last;
        if (n < 0)
        {
            do
            {
                if (b-- == buf) goto Error;
                *b = digitMap[-(int)(n % bases)];
            } while ((n /= bases) != 0);
            if (b-- == buf) goto Error;
            *b = (byte)'-';
        }
        else
        {
            do
            {
                if (b-- == buf) goto Error;
                *b = digitMap[(int)(n % bases)];
            } while ((n /= bases) != 0);
        }

        return NewString(new ReadOnlySpan<byte>(b, (int)(last - b)));

        Error: ;
        throw new IndexOutOfRangeException();
    }

    MRubyValue ConvertType(MRubyValue value, MRubyVType vtype, Symbol convertMethodId)
    {
        if (!RespondTo(value, convertMethodId))
        {
            Raise(Names.TypeError, $"can't convert type {value.VType} into {vtype}");
            return MRubyValue.Nil;
        }
        return Send(value, convertMethodId);
    }

    RString StringifyModule(RClass c)
    {
        if (c.VType == MRubyVType.SClass)
        {
            var v = c.InstanceVariables.Get(Names.AttachedKey);
            var str = v.VType == MRubyVType.Class ? StringifyAny(v) : Stringify(v);
            return NewString($"#<Class:{str}>");
        }
        return NameOf(c);
    }

    int NumberCompare(MRubyValue a, MRubyValue b)
    {
        if (a.IsInteger && b.IsInteger)
        {
            return a.IntegerValue.CompareTo(b.IntegerValue);
        }

        if (b is { IsInteger: false, IsFloat: false })
        {
            var result = Send(b, Names.OpCmp, a);
            if (!result.IsInteger) return -2;
            return (int)-result.IntegerValue;
        }

        var x = AsFloat(a);
        var y = AsFloat(b);
        return x.CompareTo(y);
    }
}