using System;
using ChibiRuby.StdLib;

namespace ChibiRuby.Serializer;

public class DateTimeFormatter : IMRubyValueFormatter<DateTime>
{
    public static readonly DateTimeFormatter Instance = new();

    public MRubyValue Serialize(DateTime value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return TimeMembers.CreateRDataFromDateTime(mrb, value);
    }

    public DateTime Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        if (TimeMembers.TryGetDateTimeOffset(value, out var dateTimeOffset))
        {
            return dateTimeOffset.DateTime;
        }
        throw new MRubySerializationException($"Cannot detect {typeof(DateTime)} from {mrb.Inspect(value)}");
    }
}

public class DateTimeOffsetFormatter : IMRubyValueFormatter<DateTimeOffset>
{
    public static readonly DateTimeOffsetFormatter Instance = new();

    public MRubyValue Serialize(DateTimeOffset value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return TimeMembers.CreateRDataFromDateTime(mrb, value);
    }

    public DateTimeOffset Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        if (TimeMembers.TryGetDateTimeOffset(value, out var dateTimeOffset))
        {
            return dateTimeOffset.DateTime;
        }
        throw new MRubySerializationException($"Cannot detect {typeof(DateTime)} from {mrb.Inspect(value)}");
    }
}

public class TimeSpanFormatter : IMRubyValueFormatter<TimeSpan>
{
    public static readonly TimeSpanFormatter Instance = new();

    public MRubyValue Serialize(TimeSpan value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        return mrb.NewString($"{value}");
    }

    public TimeSpan Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.String, "TimeSpan");
        if (TimeSpan.TryParse(mrb.Stringify(value).ToString(), out var timeSpan))
        {
            return timeSpan;
        }
        throw new MRubySerializationException($"Cannot convert to {typeof(TimeSpan)}: {mrb.Inspect(value)}");
    }
}