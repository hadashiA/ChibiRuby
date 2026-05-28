using System;
using System.Buffers;

namespace ChibiRuby.StdLib;

class MRubyRandomData
{
    public Random Random { get; private set; } = null!;
    public int Seed => seed;

    int seed;

    public MRubyRandomData()
    {
        ResetSeed();
    }

    public MRubyRandomData(int seed)
    {
        SetSeed(seed);
    }

    public void SetSeed(int seed)
    {
        this.seed = seed;
        Random = new Random(seed);
    }

    public void ResetSeed()
    {
        SetSeed(Guid.NewGuid().GetHashCode());
    }

    public MRubyValue Rand(MRubyState mrb)
    {
        if (mrb.TryGetArgumentAt(0, out var arg0))
        {
            switch (arg0.VType)
            {
                case MRubyVType.Integer:
                case MRubyVType.Float:
                    var max = mrb.AsInteger(arg0);
                    if (max <= 0)
                    {
                        return Random.NextDouble();
                    }
                    return Random.Next(0, (int)max);
                case MRubyVType.Range:
                    var range = arg0.As<RRange>();
                    if (range.Begin.IsInteger && range.End.IsInteger)
                    {
                        return RandonRangeInt(
                            (int)range.Begin.IntegerValue,
                            (int)range.End.IntegerValue,
                            range.Exclusive);
                    }
                    var begin = CastToFloat(range.Begin);
                    var end = CastToFloat(range.End);
                    return RandomRangeFloat(begin, end, range.Exclusive);
            }
        }
        return Random.NextDouble();

        MRubyValue RandonRangeInt(int begin, int end, bool exclusive)
        {
            if (end - begin <= 0) return default;
            return Random.Next(begin, end + (exclusive ? 0 : 1));
        }

        MRubyValue RandomRangeFloat(double begin, double end, bool exclusive)
        {
            var length = end - begin + (exclusive ? 0 : 1);
            if (length <= 0) return default;
            return Random.NextDouble() * length + begin;
        }

        double CastToFloat(MRubyValue value)
        {
            if (value.IsFloat) return value.FloatValue;
            if (value.IsInteger) return value.IntegerValue;
            mrb.Raise(Names.TypeError, $"no implicit conversion of {value} into Integer");
            return default; // unreached
        }
    }

    public MRubyValue SRand(MRubyState mrb)
    {
        var currentSeed = Seed;
        if (mrb.TryGetArgumentAt(0, out var seedValue))
        {
            SetSeed((int)mrb.AsInteger(seedValue));
        }
        else
        {
            ResetSeed();
        }
        return currentSeed;
    }

    public MRubyValue Bytes(MRubyState mrb)
    {
        var length = mrb.GetArgumentAsIntegerAt(0);
        if (length < 0)
        {
            mrb.Raise(Names.ArgumentError, "negative string size"u8);
        }

        if (length < 0)
        {
            mrb.Raise(Names.ArgumentError, "negative string size"u8);
        }

        var bytes = new byte[length];
        Random.NextBytes(bytes);
        return mrb.NewStringOwned(bytes);
    }
}

/// <summary>
/// Seeded pseudo-random number generator. <c>Random.new(seed)</c> creates an
/// independent stream; <c>Kernel#rand</c> and <c>Kernel#srand</c> delegate to
/// a shared default instance. Backed by .NET's <see cref="System.Random"/>
/// in ChibiRuby, so the sequence is not bit-compatible with CRuby.
/// </summary>
[RubyClass("Random")]
static class RandomMembers
{
    /// <summary>
    /// Returns a pseudo-random number using the default <c>Random</c> instance.
    /// With no argument returns a Float in [0.0, 1.0); with an Integer <c>n</c>
    /// returns an Integer in [0, n); with a Range returns a value in that range.
    /// </summary>
    /// <example>
    /// <code>
    /// Random.rand         # => Float
    /// Random.rand(10)     # => Integer in 0..9
    /// </code>
    /// </example>
    [RubyDef("(?(Integer | Float | Range[Numeric])) -> (Integer | Float)")]
    public static MRubyValue DefaultRand(MRubyState mrb, MRubyValue self)
    {
        return DefaultInstanceOf(mrb).Rand(mrb);
    }

    /// <summary>
    /// Seeds the default <c>Random</c> instance and returns the previous seed.
    /// With no argument re-seeds from the system clock.
    /// </summary>
    /// <example>
    /// <code>
    /// Random.srand(42)    # => previous seed
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Integer")]
    public static MRubyValue DefaultSRand(MRubyState mrb, MRubyValue self)
    {
        return DefaultInstanceOf(mrb).SRand(mrb);
    }

    /// <summary>
    /// Returns a String of <c>n</c> random bytes using the default <c>Random</c> instance.
    /// </summary>
    /// <example>
    /// <code>
    /// Random.bytes(4)     # => 4-byte random String
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> String")]
    public static MRubyValue DefaultBytes(MRubyState mrb, MRubyValue self)
    {
        return DefaultInstanceOf(mrb).Bytes(mrb);
    }

    /// <summary>
    /// Initializes a new <c>Random</c> instance, optionally with a fixed seed.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Random.new(42)
    /// r.rand(100)         # => Integer in 0..99
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> void")]
    public static MRubyValue Initialize(MRubyState mrb, MRubyValue self)
    {
        if (mrb.TryGetArgumentAt(0, out var seedValue))
        {
            var seed = (int)mrb.AsInteger(seedValue);
            self.As<RData>().Data = new MRubyRandomData(seed);
        }
        else
        {
            self.As<RData>().Data = new MRubyRandomData();
        }

        return self;
    }

    /// <summary>
    /// Returns a pseudo-random number using this <c>Random</c> instance.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Random.new(1)
    /// r.rand(10)          # => Integer in 0..9
    /// </code>
    /// </example>
    [RubyDef("(?(Integer | Float | Range[Numeric])) -> (Integer | Float)")]
    public static MRubyValue Rand(MRubyState mrb, MRubyValue self)
    {
        return InstanceOf(mrb, self).Rand(mrb);
    }

    /// <summary>
    /// Seeds this <c>Random</c> instance and returns the previous seed.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Random.new
    /// r.srand(42)         # => previous seed
    /// </code>
    /// </example>
    [RubyDef("(?Integer) -> Integer")]
    public static MRubyValue SRand(MRubyState mrb, MRubyValue self)
    {
        return InstanceOf(mrb, self).SRand(mrb);
    }

    /// <summary>
    /// Returns a String of <c>n</c> random bytes from this <c>Random</c> instance.
    /// </summary>
    /// <example>
    /// <code>
    /// r = Random.new(1)
    /// r.bytes(4)          # => 4-byte random String
    /// </code>
    /// </example>
    [RubyDef("(Integer) -> String")]
    public static MRubyValue Bytes(MRubyState mrb, MRubyValue self)
    {
        return InstanceOf(mrb, self).Bytes(mrb);
    }

    /// <summary>
    /// Returns a new array whose elements are the elements of <c>self</c>
    /// in random order.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].shuffle   # => [2, 1, 3]
    /// </code>
    /// </example>
    [RubyDef("(?random: Random) -> Array[untyped]")]
    public static MRubyValue ArrayShuffle(MRubyState mrb, MRubyValue self)
    {
        var array = self.As<RArray>();
        var result = array.Dup();
        ArrayShuffleBang(mrb, result);
        return result;
    }

    /// <summary>
    /// Shuffles the elements of <c>self</c> in place and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// a = [1, 2, 3]
    /// a.shuffle!          # => a (now shuffled)
    /// </code>
    /// </example>
    [RubyDef("(?random: Random) -> self")]
    public static MRubyValue ArrayShuffleBang(MRubyState mrb, MRubyValue self)
    {
        var array = self.As<RArray>();
        if (array.Length <= 1)
        {
            return self;
        }

        Random rand;
        if (mrb.TryGetKeywordArgument(mrb.Intern("random"u8), out var randomInstance))
        {
            rand = InstanceOf(mrb, randomInstance).Random;
        }
        else
        {
            rand = DefaultInstanceOf(mrb).Random;
        }
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rand.Next(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        return self;
    }

    /// <summary>
    /// Returns a random element of <c>self</c>, or an Array of <c>n</c>
    /// random elements when <c>n</c> is given.
    /// </summary>
    /// <example>
    /// <code>
    /// [1, 2, 3].sample    # => one of 1, 2, 3
    /// [1, 2, 3].sample(2) # => Array of 2 random elements
    /// </code>
    /// </example>
    [RubyDef("(?Integer, ?random: Random) -> untyped")]
    public static MRubyValue ArraySample(MRubyState mrb, MRubyValue self)
    {
        Random rand;
        if (mrb.TryGetKeywordArgument(mrb.Intern("random"u8), out var randomInstance))
        {
            rand = InstanceOf(mrb, randomInstance).Random;
        }
        else
        {
            rand = DefaultInstanceOf(mrb).Random;
        }

        var array = self.As<RArray>();
        if (mrb.TryGetArgumentAt(0, out var givenValue))
        {
            var n = (int)mrb.AsInteger(givenValue);
            // if (n > len) n = len;
            if (n < 0)
            {
                mrb.Raise(Names.ArgumentError, "negative sample number"u8);
            }

            if (n > array.Length) n =  array.Length;

            int[]? rentArray = null;
            var indices = n <= 255
                ? stackalloc int[n]
                : rentArray = ArrayPool<int>.Shared.Rent(n);

            for (var i = 0; i < n; i++)
            {
                Retry:
                var index = rand.Next(0, n);
                for (var j = 0; j < i; j++)
                {
                    if (indices[j] == index) goto Retry;
                }
                indices[i] = index;
            }

            var result = mrb.NewArray(n);
            for (var i = 0; i < n; i++)
            {
                var index = indices[i];
                result.Push(array[index]);
            }

            if (rentArray != null)
            {
                ArrayPool<int>.Shared.Return(rentArray);
            }

            return result;
        }

        switch (array.Length)
        {
            case 0:
                return default;
            case 1:
                return array[0];
            default:
                var i = rand.Next(0, array.Length);
                return array[i];
        }
    }

    static MRubyRandomData DefaultInstanceOf(MRubyState mrb)
    {
        var randomClass = mrb.GetClass(mrb.Intern("Random"u8));
        var defaultValue = mrb.GetInstanceVariable(randomClass, Names.Default);
        return (defaultValue.As<RData>().Data as MRubyRandomData)!;
    }

    static MRubyRandomData InstanceOf(MRubyState mrb, MRubyValue value)
    {
        var randomClass = mrb.GetClass(mrb.Intern("Random"u8));
        var obj = value.As<RObject>();
        if (obj.Class != randomClass)
        {
            mrb.Raise(Names.TypeError, "Not a Random instance"u8);
        }

        if (obj is RData { Data: MRubyRandomData data })
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "Not a Random instance"u8);
        return default!; // unreached
    }
}
