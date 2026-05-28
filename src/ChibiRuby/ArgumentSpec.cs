using System;

namespace ChibiRuby;

/// <summary>
///
/// </summary>
/// <remarks>
/// bit layout:
/// |Req|Req|Req|Req|Req|Opt|Opt|Opt|Opt|Opt|Res|Post|Post|Post|Post|Post|K|K|K|K|K|KDict|Block|
/// </remarks>
public readonly struct ArgumentSpec(uint bits) : IEquatable<ArgumentSpec>
{
    public static readonly ArgumentSpec None = new(0);
    public static readonly ArgumentSpec Any = new() { TakeRestArguments = true };

    public readonly uint Bits = bits;

    /// <summary>
    /// Number of required arguments
    /// </summary>
    public byte MandatoryArguments1Count
    {
        get => (byte)((Bits >> 18) & 0x1f);
        init => Bits |= (byte)((value & 0x1f) << 18);
    }

    /// <summary>
    /// Number of optional arguments
    /// </summary>
    public byte OptionalArgumentsCount
    {
        get => (byte)((Bits >> 13) & 0x1f);
        init => Bits |= (byte)((value & 0x1f) << 13);
    }

    /// <summary>
    /// Take a rest arguments
    /// </summary>
    public bool TakeRestArguments
    {
        get => ((Bits >> 12) & 1) == 1;
        init => Bits |= (byte)((value ? 1 : 0) << 12);
    }

    /// <summary>
    /// Nubmer of post requires arguments (after rest)
    /// </summary>
    public byte MandatoryArguments2Count
    {
        get => (byte)((Bits >> 7) & 0x1f);
        init => Bits |= (byte)((value & 0x1f) << 7);
    }

    /// <summary>
    /// Number of keyword arguments
    /// </summary>
    public byte KeywordArgumentsCount
    {
        get => (byte)((Bits >> 2) & 0x1f);
        init => Bits |= (byte)((value & 0x1f) << 2);
    }

    /// <summary>
    /// Take a keyword dictionary
    /// </summary>
    public bool TakeKeywordDict
    {
        get => ((Bits >> 1) & 1) == 1;
        init => Bits |= (byte)((value ? 1 : 0) << 1);
    }

    /// <summary>
    /// Take a block argument
    /// </summary>
    public bool TakeBlock
    {
        get => (Bits & 1) == 1;
        init => Bits |= (byte)(value ? 1 : 0);
    }

    public bool Equals(ArgumentSpec other)
    {
        return Bits == other.Bits;
    }

    public override bool Equals(object? obj)
    {
        return obj is ArgumentSpec other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (int)Bits;
    }
}
