using System;
using System.Globalization;
using Utf8StringInterpolation;

namespace ChibiRuby.StdLib;

public enum MRubyTimeZone
{
    None,
    Utc,
    Local,
    Last,
}

/// <summary>
/// A mutable reference to DateTime that is encapsulated in RData and can be mutation from the outside.
/// </summary>
class MRubyTimeData(DateTimeOffset dateTimeOffset) :
    IEquatable<MRubyTimeData>,
    IComparable<MRubyTimeData>
{
    readonly TimeSpan offset = dateTimeOffset.Offset;

    public DateTimeOffset DateTimeOffset { get; set; } = dateTimeOffset;

    public long Ticks
    {
        get => DateTimeOffset.Ticks;
        set => DateTimeOffset = new DateTimeOffset(value, offset);
    }

    public MRubyTimeZone TimeZone => offset.Ticks > 0
        ? MRubyTimeZone.Local
        : MRubyTimeZone.Utc;

    public bool Equals(MRubyTimeData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return DateTimeOffset.Ticks == other.DateTimeOffset.Ticks; // ignore timezone
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((MRubyTimeData)obj);
    }

    public override int GetHashCode()
    {
        return DateTimeOffset.Ticks.GetHashCode();
    }

    public int CompareTo(MRubyTimeData? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        return Ticks.CompareTo(other.Ticks);
    }
}

/// <summary>
/// A point in time, accurate to sub-second precision. Created with
/// <c>Time.now</c>, <c>Time.at</c>, or <c>Time.utc</c>/<c>Time.local</c>;
/// supports arithmetic with <c>Numeric</c> seconds and comparison via
/// <c>&lt;=&gt;</c>. Backed by .NET's <see cref="System.DateTimeOffset"/>
/// in ChibiRuby.
/// </summary>
[RubyClass("Time")]
static class TimeMembers
{
    const long TicksPerMicrosecond = 10;

    public static RData CreateRDataFromDateTime(MRubyState mrb, DateTimeOffset dateTimeOffset)
    {
        var timeClass = mrb.GetConst(mrb.Intern("Time"u8), mrb.ObjectClass).As<RClass>();
        var data = new MRubyTimeData(dateTimeOffset);
        return new RData(timeClass, data);
    }

    public static bool TryGetDateTimeOffset(MRubyValue value, out DateTimeOffset dateTimeOffset)
    {
        if (TryGetTimeData(value, out var data))
        {
            dateTimeOffset = data.DateTimeOffset;
            return true;
        }
        dateTimeOffset = default;
        return false;
    }

    /// <summary>
    /// Returns a new <c>Time</c> object representing the current local time.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.now
    /// t.class    # => Time
    /// </code>
    /// </example>
    [RubyDef("() -> Time")]
    public static MRubyValue Now(MRubyState mrb, MRubyValue _)
    {
        return CreateRDataFromDateTime(mrb, DateTimeOffset.Now);
    }

    /// <summary>
    /// Returns a new <c>Time</c> object representing the given Unix epoch seconds, with an optional microseconds component.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.at(0).utc           # => 1970-01-01 00:00:00 UTC
    /// Time.at(1_700_000_000)   # local time at that epoch second
    /// </code>
    /// </example>
    [RubyDef("(Numeric, ?Numeric) -> Time")]
    public static MRubyValue CreateAt(MRubyState mrb, MRubyValue _)
    {
        var secValue = mrb.GetArgumentAt(0);

        var ticks = ConvertToTicks(mrb, secValue, true);

        if (mrb.TryGetArgumentAt(1, out var usecValue))
        {
            ticks += ConvertToTicks(mrb, usecValue, false) / 1_000_000;
        }

        ticks += DateTime.UnixEpoch.ToLocalTime().Ticks;

        DateTimeOffset dateTimeOffset;
        try
        {
            dateTimeOffset = new DateTime(ticks, DateTimeKind.Local);
        }
        catch (ArgumentException)
        {
            mrb.Raise(Names.ArgumentError, "out of time range"u8);
            throw; // unreached
        }
        return CreateRDataFromDateTime(mrb, dateTimeOffset);
    }

    /// <summary>
    /// Returns a new <c>Time</c> object in UTC built from the given year and (optionally) month, day, hour, minute, second, and microsecond.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15, 10, 30, 0)
    /// t.year     # => 2024
    /// t.utc?     # => true
    /// </code>
    /// </example>
    [RubyDef("(Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer) -> Time")]
    public static MRubyValue CreateUtc(MRubyState mrb, MRubyValue _)
    {
        var year = (int)mrb.GetArgumentAsIntegerAt(0);
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var sec = 0;
        var usec = 0;

        if (mrb.TryGetArgumentAt(1, out var monthValue))
        {
            month = (int)mrb.AsInteger(monthValue);
        }
        if (mrb.TryGetArgumentAt(2, out var dayValue))
        {
            day = (int)mrb.AsInteger(dayValue);
        }
        if (mrb.TryGetArgumentAt(3, out var hourValue))
        {
            hour = (int)mrb.AsInteger(hourValue);
        }
        if (mrb.TryGetArgumentAt(4, out var minuteValue))
        {
            minute = (int)mrb.AsInteger(minuteValue);
        }

        if (mrb.TryGetArgumentAt(5, out var secValue))
        {
            sec = (int)mrb.AsInteger(secValue);
        }
        if (mrb.TryGetArgumentAt(6, out var usecValue))
        {
            usec = (int)mrb.AsInteger(usecValue);
        }
        var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Utc);
        dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
        return CreateRDataFromDateTime(mrb, dateTime);
    }

    /// <summary>
    /// Returns a new <c>Time</c> object in the local timezone built from the given year and (optionally) month, day, hour, minute, second, and microsecond.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.local(2024, 1, 15, 10, 30, 0)
    /// t.year     # => 2024
    /// t.utc?     # => false
    /// </code>
    /// </example>
    [RubyDef("(Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer) -> Time")]
    public static MRubyValue CreateLocal(MRubyState mrb, MRubyValue _)
    {
        var year = (int)mrb.GetArgumentAsIntegerAt(0);
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var sec = 0;
        var usec = 0;

        if (mrb.TryGetArgumentAt(1, out var monthValue))
        {
            month = (int)mrb.AsInteger(monthValue);
        }
        if (mrb.TryGetArgumentAt(2, out var dayValue))
        {
            day = (int)mrb.AsInteger(dayValue);
        }
        if (mrb.TryGetArgumentAt(3, out var hourValue))
        {
            hour = (int)mrb.AsInteger(hourValue);
        }
        if (mrb.TryGetArgumentAt(4, out var minuteValue))
        {
            minute = (int)mrb.AsInteger(minuteValue);
        }

        if (mrb.TryGetArgumentAt(5, out var secValue))
        {
            sec = (int)mrb.AsInteger(secValue);
        }
        if (mrb.TryGetArgumentAt(6, out var usecValue))
        {
            usec = (int)mrb.AsInteger(usecValue);
        }
        var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Local);
        dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
        return CreateRDataFromDateTime(mrb, dateTime);
    }

    /// <summary>
    /// Initializes a newly allocated <c>Time</c>. With no arguments returns the current time; otherwise parses year, month, day, hour, minute, second, microsecond as local time.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.new
    /// t.class    # => Time
    /// </code>
    /// </example>
    [RubyDef("(?Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer, ?Integer) -> void")]
    public static MRubyValue Initialize(MRubyState mrb, MRubyValue self)
    {
        DateTimeOffset dateTimeOffset;
        if (mrb.GetArgumentCount() <= 0)
        {
            dateTimeOffset = DateTimeOffset.Now;
        }
        else
        {
            var year = 0;
            var month = 1;
            var day = 1;
            var hour = 0;
            var minute = 0;
            var sec = 0;
            var usec = 0;

            if (mrb.TryGetArgumentAt(1, out var yearValue))
            {
                year = (int)mrb.AsInteger(yearValue);
            }
            if (mrb.TryGetArgumentAt(2, out var monthValue))
            {
                month = (int)mrb.AsInteger(monthValue);
            }
            if (mrb.TryGetArgumentAt(3, out var dayValue))
            {
                day = (int)mrb.AsInteger(dayValue);
            }
            if (mrb.TryGetArgumentAt(4, out var hourValue))
            {
                hour = (int)mrb.AsInteger(hourValue);
            }
            if (mrb.TryGetArgumentAt(5, out var minuteValue))
            {
                minute = (int)mrb.AsInteger(minuteValue);
            }
            if (mrb.TryGetArgumentAt(6, out var secValue))
            {
                sec = (int)mrb.AsInteger(secValue);
            }
            if (mrb.TryGetArgumentAt(7, out var usecValue))
            {
                usec = (int)mrb.AsInteger(usecValue);
            }

            var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Local);
            dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
            dateTimeOffset = new DateTimeOffset(dateTime);
        }
        self.As<RData>().Data = CreateRDataFromDateTime(mrb, dateTimeOffset);
        return self;
    }

    /// <summary>
    /// Copies the state of the given <c>Time</c> into <c>self</c>. Used by <c>dup</c> and <c>clone</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15)
    /// t.dup == t   # => true
    /// </code>
    /// </example>
    [RubyDef("(Time) -> self")]
    public static MRubyValue InitializeCopy(MRubyState mrb, MRubyValue self)
    {
        var copyValue = mrb.GetArgumentAt(0);
        if (mrb.ValueEquals(copyValue, self)) return copyValue;

        if (!mrb.InstanceOf(copyValue, mrb.ClassOf(self)))
        {
            mrb.Raise(Names.TypeError, "wrong argument class"u8);
        }

        var src = GetTimeData(mrb, self);

        DateTimeOffset dateTimeOffset;
        if (copyValue.As<RData>().Data is MRubyTimeData copy)
        {
            dateTimeOffset = copy.DateTimeOffset;
        }
        else
        {
            dateTimeOffset = DateTimeOffset.Now;
        }
        src.DateTimeOffset = dateTimeOffset;
        return copyValue;
    }

    /// <summary>
    /// Returns a hash code for <c>self</c> derived from its internal tick count.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).hash.class   # => Integer
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Hash(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).Ticks.GetHashCode();
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> and the argument refer to the same instant.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15) == Time.utc(2024, 1, 15)   # => true
    /// Time.utc(2024, 1, 15) == Time.utc(2024, 1, 16)   # => false
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> bool")]
    public static MRubyValue OpEq(MRubyState mrb, MRubyValue self)
    {
        if (!TryGetTimeData(mrb.GetArgumentAt(0), out var otherTime))
        {
            return false;
        }
        var selfTime = GetTimeData(mrb, self);
        return selfTime.Equals(otherTime);
    }

    /// <summary>
    /// Compares <c>self</c> with the given <c>Time</c>. Returns -1, 0, 1, or <c>nil</c> if the argument is not a <c>Time</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15) &lt;=&gt; Time.utc(2024, 1, 16)   # => -1
    /// Time.utc(2024, 1, 15) &lt;=&gt; "foo"                   # => nil
    /// </code>
    /// </example>
    [RubyDef("(untyped) -> Integer?")]
    public static MRubyValue OpCmp(MRubyState mrb, MRubyValue self)
    {
        if (!TryGetTimeData(mrb.GetArgumentAt(0), out var otherTime))
        {
            return default;
        }
        var selfTime = GetTimeData(mrb, self);
        return selfTime.CompareTo(otherTime);
    }

    /// <summary>
    /// Returns a new <c>Time</c> shifted forward by the given number of seconds.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15, 10, 0, 0)
    /// (t + 60).min   # => 1
    /// </code>
    /// </example>
    [RubyDef("(Numeric) -> Time")]
    public static MRubyValue OpAdd(MRubyState mrb, MRubyValue self)
    {
        var time = GetTimeData(mrb, self);
        var ticksAdd = ConvertToTicks(mrb, mrb.GetArgumentAt(0), true);

        long newTicks;
        try
        {
            checked
            {
                newTicks = time.Ticks + ticksAdd;
            }
        }
        catch (OverflowException)
        {
            mrb.Raise(Names.RangeError, $"Time out of range in addition");
            throw;
        }

        var result = new DateTimeOffset(newTicks, time.DateTimeOffset.Offset);
        return CreateRDataFromDateTime(mrb, result);
    }

    /// <summary>
    /// Subtracts seconds or another <c>Time</c> from <c>self</c>. Returns a new <c>Time</c> when given a number, or the difference in seconds when given a <c>Time</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15, 10, 0, 0)
    /// (t - 60).min                          # => 59
    /// t - Time.utc(2024, 1, 15, 9, 0, 0)    # => 3600
    /// </code>
    /// </example>
    [RubyDef("(Time) -> Integer | (Numeric) -> Time")]
    public static MRubyValue OpSub(MRubyState mrb, MRubyValue self)
    {
        var time = GetTimeData(mrb, self);

        var arg0 = mrb.GetArgumentAt(0);
        if (TryGetTimeData(arg0,  out var other))
        {
            var diff = time.DateTimeOffset - other.DateTimeOffset;
            return diff.Ticks / TimeSpan.TicksPerSecond;
        }

        var ticksSub = ConvertToTicks(mrb, arg0, true);
        long newTicks;
        try
        {
            checked
            {
                newTicks = time.Ticks - ticksSub;
            }
        }
        catch (OverflowException)
        {
            mrb.Raise(Names.RangeError, $"Time out of range in subtraction");
            throw;
        }

        DateTimeOffset result;
        try
        {
            result = new DateTimeOffset(newTicks, time.DateTimeOffset.Offset);
        }
        catch (ArgumentException)
        {
            mrb.Raise(Names.RangeError, $"Time out of range in subtraction");
            throw; // unreached
        }
        return CreateRDataFromDateTime(mrb, result);
    }

    /// <summary>
    /// Returns a canonical representation of <c>self</c> in the form "Day Mon DD HH:MM:SS YYYY".
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15, 10, 30, 0).asctime   # => "Mon Jan 15 10:30:00 2024"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Asctime(MRubyState mrb, MRubyValue self)
    {
        var d = GetTimeData(mrb, self).DateTimeOffset;
        using var buffer = Utf8String.CreateWriter(out var writer, CultureInfo.InvariantCulture);
        writer.AppendFormat($"{d:ddd} {d:MMM} {d.Day,2} {d:HH}:{d:mm}:{d:ss} {d:yyyy}");
        writer.Flush();
        return mrb.NewString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Returns a string representation of <c>self</c> in "YYYY-MM-DD HH:MM:SS ZONE" form.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15, 10, 30, 0).to_s   # => "2024-01-15 10:30:00 UTC"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue ToS(MRubyState mrb, MRubyValue self)
    {
        var data = GetTimeData(mrb, self);
        var t = data.DateTimeOffset;
        if (t.Offset == TimeSpan.Zero)
        {
            // utc
            return mrb.NewString($"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00} UTC");
        }
        // local
        return mrb.NewString($"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00} +{t.Offset.Hours:00}00");
    }

    /// <summary>
    /// Returns the number of seconds since the Unix epoch as a <c>Float</c>, including the fractional part.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(1970, 1, 1, 0, 0, 1).to_f   # => 1.0
    /// </code>
    /// </example>
    [RubyDef("() -> Float")]
    public static MRubyValue ToF(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return (dateTimeOffset - DateTimeOffset.UnixEpoch).TotalSeconds;
    }

    /// <summary>
    /// Returns the number of whole seconds since the Unix epoch.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(1970, 1, 1, 0, 0, 1).to_i   # => 1
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue ToI(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Returns the offset from UTC of <c>self</c> in seconds.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).utc_offset   # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue UtcOffset(MRubyState mrb, MRubyValue self)
    {
        return (int)GetTimeData(mrb, self).DateTimeOffset.Offset.TotalSeconds;
    }

    /// <summary>
    /// Returns the four-digit year of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).year   # => 2024
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Year(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Year;

    /// <summary>
    /// Returns the month of <c>self</c> (1..12).
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).month   # => 1
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Month(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Month;

    /// <summary>
    /// Returns the day of the month (1..31) of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).day   # => 15
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Day(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Day;

    /// <summary>
    /// Returns the hour of the day (0..23) of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15, 10, 30, 0).hour   # => 10
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Hour(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Hour;

    /// <summary>
    /// Returns the minute of the hour (0..59) of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15, 10, 30, 0).min   # => 30
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Minute(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Minute;

    /// <summary>
    /// Returns the second of the minute (0..59) of <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15, 10, 30, 45).sec   # => 45
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Second(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Second;

    /// <summary>
    /// Returns the microsecond component of <c>self</c> (0..999_999).
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).usec   # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue MicroSecond(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Millisecond * 1_000 +
               (int)((dateTimeOffset.Ticks / TicksPerMicrosecond) % 1000);
    }

    /// <summary>
    /// Returns the nanosecond component of <c>self</c> (0..999_999_999).
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).nsec   # => 0
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue NanoSecond(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Millisecond * 1_000_000 +
               (int)((dateTimeOffset.Ticks / TicksPerMicrosecond) % 1_000) * 1_000 +
               (dateTimeOffset.Ticks % TicksPerMicrosecond) * 100;
    }

    /// <summary>
    /// Returns the day of the week (0..6, where 0 is Sunday) for <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).wday   # => 1  (Monday)
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Wday(MRubyState mrb, MRubyValue self) =>
        (int)GetTimeData(mrb, self).DateTimeOffset.DayOfWeek;

    /// <summary>
    /// Returns the day of the year (1..366) for <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).yday   # => 15
    /// </code>
    /// </example>
    [RubyDef("() -> Integer")]
    public static MRubyValue Yday(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.DayOfYear;

    /// <summary>
    /// Returns the timezone name of <c>self</c>: "UTC" for UTC, otherwise the signed hour-and-minute offset like "+0900".
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).zone   # => "UTC"
    /// </code>
    /// </example>
    [RubyDef("() -> String")]
    public static MRubyValue Zone(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        if (dateTimeOffset.Offset == TimeSpan.Zero)
        {
            return mrb.NewString("UTC"u8);
        }

        Span<byte> result = stackalloc byte[5];

        var format = Utf8String.Format($"{dateTimeOffset:zzz}");
        format[0..3].CopyTo(result);
        format[4..6].CopyTo(result[3..]);
        return mrb.NewString(result);
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is in UTC (zero offset).
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).utc?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QUtc(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset =  GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Offset == TimeSpan.Zero;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Sunday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 14).sunday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QSunday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Sunday;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Monday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 15).monday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QMonday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Monday;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Tuesday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 16).tuesday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyMethod QTuesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Tuesday;
    });

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Wednesday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 17).wednesday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyMethod QWednesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Wednesday;
    });

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Thursday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 18).thursday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyMethod QThursday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Thursday;
    });

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Friday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 19).friday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyMethod QFriday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Friday;
    });

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> falls on a Saturday.
    /// </summary>
    /// <example>
    /// <code>
    /// Time.utc(2024, 1, 20).saturday?   # => true
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QSaturday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Saturday;
    }

    /// <summary>
    /// Returns <c>true</c> when <c>self</c> is in a daylight-saving-time period for the local timezone.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.local(2024, 7, 15)
    /// t.dst?    # => true or false depending on the local timezone
    /// </code>
    /// </example>
    [RubyDef("() -> bool")]
    public static MRubyValue QDaylightSavintTime(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return TimeZoneInfo.Local.IsDaylightSavingTime(dateTimeOffset);
    }

    /// <summary>
    /// Returns a new <c>Time</c> representing the same instant converted to UTC. Does not mutate <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.local(2024, 1, 15)
    /// t.getutc.utc?    # => true
    /// </code>
    /// </example>
    [RubyDef("() -> Time")]
    public static MRubyValue GetUtc(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        return CreateRDataFromDateTime(mrb, t.DateTimeOffset.ToUniversalTime());
    }

    /// <summary>
    /// Returns a new <c>Time</c> representing the same instant converted to local time. Does not mutate <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15)
    /// t.getlocal.utc?  # => false
    /// </code>
    /// </example>
    [RubyDef("() -> Time")]
    public static MRubyValue GetLocal(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        return CreateRDataFromDateTime(mrb, t.DateTimeOffset.ToLocalTime());
    }

    /// <summary>
    /// Converts <c>self</c> in place to UTC and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.local(2024, 1, 15)
    /// t.utc.utc?       # => true
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue ConvertToUtc(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        t.DateTimeOffset = t.DateTimeOffset.ToUniversalTime();
        return self;
    }

    /// <summary>
    /// Converts <c>self</c> in place to local time and returns <c>self</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// t = Time.utc(2024, 1, 15)
    /// t.localtime.utc?  # => false
    /// </code>
    /// </example>
    [RubyDef("() -> self")]
    public static MRubyValue ConvertToLocal(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        t.DateTimeOffset = t.DateTimeOffset.ToLocalTime();
        return self;
    }

    static bool TryGetTimeData(MRubyValue value, out MRubyTimeData data)
    {
        if (value.Object is RData { Data: MRubyTimeData timeData })
        {
            data = timeData;
            return true;
        }

        data = default!;
        return false;
    }

    static MRubyTimeData GetTimeData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetTimeData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.ArgumentError, "uninitialized Time"u8);
        return default!; // unreachable
    }


    static long ConvertToTicks(MRubyState mrb, MRubyValue secValue, bool withUSecs)
    {
        var ticks = 0L;
        if (secValue.IsFloat)
        {
            var sec = secValue.FloatValue;
            mrb.EnsureExactValue(sec);

            if (sec is >= long.MaxValue - 1.0 or < long.MinValue + 1.0)
            {
                mrb.Raise(Names.ArgumentError, $"{sec} out of Time range");
            }
            if (withUSecs)
            {
                var secFloored = Math.Floor(sec);
                ticks = (long)secFloored * TimeSpan.TicksPerSecond;
                ticks += (long)Math.Truncate((sec - secFloored) * TicksPerMicrosecond);
            }
            else
            {
                ticks = (long)Math.Round(sec) * TimeSpan.TicksPerSecond;
            }
        }
        else if (secValue.IsInteger)
        {
            ticks = secValue.IntegerValue * TimeSpan.TicksPerSecond;
        }
        else
        {
            mrb.Raise(Names.TypeError, $"cannot convert {mrb.Stringify(secValue)} to time");
        }
        return ticks;
    }
}
