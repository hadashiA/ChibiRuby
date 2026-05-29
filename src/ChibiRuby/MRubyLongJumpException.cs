using System;

namespace ChibiRuby;

public class MRubyLongJumpException(string message) : Exception(message);

public class MRubyBreakException(MRubyState state, RBreak breakObject)
    : MRubyLongJumpException("break")
{
    public MRubyState State => state;
    public RBreak BreakObject => breakObject;
}