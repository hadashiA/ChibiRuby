using System;
using System.Globalization;
using Utf8StringInterpolation;

namespace MRubyCS.StdLib;

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

    [RubyDef("() -> Time")]
    public static MRubyValue Now(MRubyState mrb, MRubyValue _)
    {
        return CreateRDataFromDateTime(mrb, DateTimeOffset.Now);
    }

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

    [RubyDef("() -> Integer")]
    public static MRubyValue Hash(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).Ticks.GetHashCode();
    }

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

    [RubyDef("() -> String")]
    public static MRubyValue Asctime(MRubyState mrb, MRubyValue self)
    {
        var d = GetTimeData(mrb, self).DateTimeOffset;
        using var buffer = Utf8String.CreateWriter(out var writer, CultureInfo.InvariantCulture);
        writer.AppendFormat($"{d:ddd} {d:MMM} {d.Day,2} {d:HH}:{d:mm}:{d:ss} {d:yyyy}");
        writer.Flush();
        return mrb.NewString(buffer.WrittenSpan);
    }

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

    [RubyDef("() -> Float")]
    public static MRubyValue ToF(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return (dateTimeOffset - DateTimeOffset.UnixEpoch).TotalSeconds;
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue ToI(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.ToUnixTimeSeconds();
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue UtcOffset(MRubyState mrb, MRubyValue self)
    {
        return (int)GetTimeData(mrb, self).DateTimeOffset.Offset.TotalSeconds;
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Year(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Year;

    [RubyDef("() -> Integer")]
    public static MRubyValue Month(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Month;

    [RubyDef("() -> Integer")]
    public static MRubyValue Day(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Day;

    [RubyDef("() -> Integer")]
    public static MRubyValue Hour(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Hour;

    [RubyDef("() -> Integer")]
    public static MRubyValue Minute(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Minute;

    [RubyDef("() -> Integer")]
    public static MRubyValue Second(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.Second;

    [RubyDef("() -> Integer")]
    public static MRubyValue MicroSecond(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Millisecond * 1_000 +
               (int)((dateTimeOffset.Ticks / TicksPerMicrosecond) % 1000);
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue NanoSecond(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Millisecond * 1_000_000 +
               (int)((dateTimeOffset.Ticks / TicksPerMicrosecond) % 1_000) * 1_000 +
               (dateTimeOffset.Ticks % TicksPerMicrosecond) * 100;
    }

    [RubyDef("() -> Integer")]
    public static MRubyValue Wday(MRubyState mrb, MRubyValue self) =>
        (int)GetTimeData(mrb, self).DateTimeOffset.DayOfWeek;

    [RubyDef("() -> Integer")]
    public static MRubyValue Yday(MRubyState mrb, MRubyValue self) =>
        GetTimeData(mrb, self).DateTimeOffset.DayOfYear;

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

    [RubyDef("() -> bool")]
    public static MRubyValue QUtc(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset =  GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Offset == TimeSpan.Zero;
    }

    [RubyDef("() -> bool")]
    public static MRubyValue QSunday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Sunday;
    }

    [RubyDef("() -> bool")]
    public static MRubyValue QMonday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Monday;
    }

    [RubyDef("() -> bool")]
    public static MRubyMethod QTuesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Tuesday;
    });

    [RubyDef("() -> bool")]
    public static MRubyMethod QWednesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Wednesday;
    });

    [RubyDef("() -> bool")]
    public static MRubyMethod QThursday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Thursday;
    });

    [RubyDef("() -> bool")]
    public static MRubyMethod QFriday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Friday;
    });

    [RubyDef("() -> bool")]
    public static MRubyValue QSaturday(MRubyState mrb, MRubyValue self)
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Saturday;
    }

    [RubyDef("() -> bool")]
    public static MRubyValue QDaylightSavintTime(MRubyState mrb, MRubyValue self)
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return TimeZoneInfo.Local.IsDaylightSavingTime(dateTimeOffset);
    }

    [RubyDef("() -> Time")]
    public static MRubyValue GetUtc(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        return CreateRDataFromDateTime(mrb, t.DateTimeOffset.ToUniversalTime());
    }

    [RubyDef("() -> Time")]
    public static MRubyValue GetLocal(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        return CreateRDataFromDateTime(mrb, t.DateTimeOffset.ToLocalTime());
    }

    [RubyDef("() -> self")]
    public static MRubyValue ConvertToUtc(MRubyState mrb, MRubyValue self)
    {
        var t = GetTimeData(mrb, self);
        t.DateTimeOffset = t.DateTimeOffset.ToUniversalTime();
        return self;
    }

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
