$undefined = Object.new
$asserts = []

##
# Verify a code block.
#
# str : A remark which will be printed in case
#       this assertion fails
# iso : The ISO reference code of the feature
#       which will be tested by this
#       assertion
def assert(str = 'assert', iso = '')
  $asserts.clear
  begin
    yield
  rescue Exception => e
    $asserts.push [false, "#{e.class}: #{e.message} \n#{e.backtrace}"]
  ensure
    __report_result str, iso, $asserts
  end
end

def assert_true(obj, msg = nil, diff = nil)
  unless obj == true
    diff ||= "    Expected #{obj.inspect} to be true."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_false(obj, msg = nil, diff = nil)
  unless obj == false
    diff ||= "    Expected #{obj.inspect} to be false."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_nil(obj, msg = nil)
  unless ret = obj.nil?
    diff = "    Expected #{obj.inspect} to be nil."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_not_nil(obj, msg = nil)
  if ret = obj.nil?
    diff = "    Expected #{obj.inspect} to not be nil."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_equal(exp, act_or_msg = nil, msg = nil, &block)
  ret, exp, act, msg = _eval_assertion(:==, exp, act_or_msg, msg, block)
  if ret
    $asserts.push [true, msg]
  else
    diff = _assertion_diff(exp, act)
    $asserts.push [false, msg, diff]
  end
end

def assert_not_equal(exp, act_or_msg = nil, msg = nil, &block)
  ret, exp, act, msg = _eval_assertion(:==, exp, act_or_msg, msg, block)
  if ret
    diff = "    Expected #{act.inspect} to not be equal to #{exp.inspect}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_raise(*exc)
  msg = (exc.last.is_a? String) ? exc.pop : nil
  exc = exc.empty? ? StandardError : exc.size == 1 ? exc[0] : exc
  begin
    yield
  rescue *exc => e
    $asserts.push [true, msg]
  rescue Exception => e
    diff = "    #{exc} exception expected, not\n" \
           "    Class: <#{e.class}>\n" \
           "    Message: <#{e}>"
    $asserts.push [false, msg, diff]
  else
    diff = "    #{exc} expected but nothing was raised."
    $asserts.push [false, msg, diff]
  end
end

def assert_nothing_raised(msg = nil)
  begin
    yield
  rescue Exception => e
    diff = "    Exception raised:\n" \
           "    Class: <#{e.class}>\n" \
           "    Message: <#{e}>"
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_predicate(*args) = _assert_predicate(true, *args)
def assert_not_predicate(*args) = _assert_predicate(false, *args)

def assert_same(*args); _assert_same(true, *args) end
def assert_not_same(*args); _assert_same(false, *args) end

def assert_raise_with_message(*args, &block)
  _assert_raise_with_message(:plain, *args, &block)
end

def assert_raise_with_message_pattern(*args, &block)
  _assert_raise_with_message(:pattern, *args, &block)
end

def assert_kind_of(cls, obj, msg = nil)
  unless ret = obj.kind_of?(cls)
    diff = "    Expected #{obj.inspect} to be a kind of #{cls}, not #{obj.class}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def assert_match(*args); _assert_match(true, *args) end
def assert_not_match(*args); _assert_match(false, *args) end

def assert_include(*args); _assert_include(true, *args) end
def assert_not_include(*args); _assert_include(false, *args) end

def assert_operator(*args); _assert_operator(true, *args) end
def assert_not_operator(*args); _assert_operator(false, *args) end

# Fails unless +exp+ is equal to +act+ in terms of a Float
def assert_float(exp, act, msg = nil)
  e, a = exp.to_f, act.to_f
  if e.finite? && a.finite? && (n = (e - a).abs) > FLOAT_TOLERANCE
    $asserts.push ["    Expected |#{exp} - #{act}| (#{n}) to be <= #{FLOAT_TOLERANCE}."]
  elsif (e.infinite? || a.infinite?) && e != a ||
    e.nan? && !a.nan? || !e.nan? && a.nan?
    $asserts.push [false, msg, "    Expected #{act} to be #{exp}."]
  else
    $asserts.push [true, msg]
  end
end

def pass
  assert_true(true)
end

def _assert_operator(affirmed, obj1, op, obj2 = $undefined, msg = nil)
  return _assert_predicate(affirmed, obj1, op, msg) if $undefined.equal?(obj2)
  unless ret = obj1.__send__(op, obj2) == affirmed
    diff = "    Expected #{obj1.inspect} to #{'not ' unless affirmed}be #{op} #{obj2.inspect}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def _assert_include(affirmed, collection, obj, msg = nil)
  unless ret = collection.include?(obj) == affirmed
    diff = "    Expected #{collection.inspect} to #{'not ' unless affirmed}include #{obj.inspect}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def _assert_match(affirmed, pattern, str, msg = nil)
  unless ret = _str_match?(pattern, str) == affirmed
    diff = "    Expected #{pattern.inspect} to #{'not ' unless affirmed}match #{str.inspect}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def _assert_raise_with_message(type, exc, exp_msg, msg = nil, &block)
  begin
    yield
  rescue exc => e
    $asserts.push [true, msg]
  rescue Exception => e
    diff = "    #{exc} exception expected, not\n" \
           "    Class: <#{e.class}>\n" \
           "    Message: <#{e}>"
    err_msg = e.message
    unless ret = type == :pattern ? _str_match?(exp_msg, err_msg) : exp_msg == err_msg
      diff = "    Expected Exception(#{exc}) was raised, but the message doesn't match.\n"
      if type == :pattern
        diff += "    Expected #{exp_msg.inspect} to match #{err_msg.inspect}."
      else
        diff += assertion_diff(exp_msg, err_msg)
      end
      $asserts.push [false, msg, diff]
    else
      $asserts.push [true, msg]
    end

  else
    diff = "    #{exc} expected but nothing was raised."
    $asserts.push [false, msg, diff]
  end
end

def _assert_same(affirmed, exp, act, msg = nil)
  unless ret = exp.equal?(act) == affirmed
    exp_str, act_str = [exp, act].map do |o|
      "#{o.inspect} (class=#{o.class}, oid=#{o.__id__})"
    end
    diff = "    Expected #{act_str} to #{'not ' unless affirmed}be the same as #{exp_str}."
    $asserts.push [false, msg, diff]
  else
    $asserts.push [true, msg]
  end
end

def _assert_predicate(affirmed, obj, op, msg = nil)
  unless ret = obj.__send__(op) == affirmed
    diff = "    Expected #{obj.inspect} to #{'not ' unless affirmed}be #{op}."
  end
  if ret
    $asserts.push [true, msg]
  else
    $asserts.push [false, msg, diff]
  end
end

def _eval_assertion(meth, exp, act_or_msg, msg, block)
  if block
    exp, act, msg = exp, block.call, act_or_msg
  else
    exp, act, msg = exp, act_or_msg, msg
  end
  return exp.__send__(meth, act), exp, act, msg
end

def _assertion_diff(exp, act)
  "    Expected: #{exp.inspect}\n" \
  "      Actual: #{act.inspect}"
end

##
# Skip the test
class MRubyTestSkip < NotImplementedError; end

# def skip(cause = "")
#   raise MRubyTestSkip.new(cause)
# end