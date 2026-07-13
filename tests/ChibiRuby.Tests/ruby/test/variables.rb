##
# Variable access tests, including the interpreter's inline-cache edge cases
# (slot guesses must be re-verified when a table's layout changes).

assert('GetIV/SetIV inline cache survives remove_instance_variable') do
  class IvIcRemove
    def initialize
      @a = 1
      @b = 2
      @c = 3
    end
    def get_c
      @c
    end
    def set_c(v)
      @c = v
    end
    def drop_b
      remove_instance_variable(:@b)
    end
  end

  o = IvIcRemove.new
  assert_equal 3, o.get_c   # warm the call site: @c found at slot 2
  o.drop_b                  # @c shifts down to slot 1
  assert_equal 3, o.get_c   # stale slot guess must be rejected
  o.set_c(30)
  assert_equal 30, o.get_c
end

assert('ivar site alternating between different layouts') do
  class IvIcWide
    def initialize
      @pad0 = 0
      @pad1 = 0
      @x = 100
    end
    def get_x
      @x
    end
  end
  class IvIcNarrow
    def initialize
      @x = 200
    end
    def get_x
      @x
    end
  end

  wide = IvIcWide.new
  narrow = IvIcNarrow.new
  acc = 0
  i = 0
  while i < 100
    acc += wide.get_x + narrow.get_x
    i += 1
  end
  assert_equal 30_000, acc
end

assert('ivar read before assignment returns nil through a warmed site') do
  class IvIcLazy
    def get_v
      @v
    end
    def set_v(v)
      @v = v
    end
  end

  o = IvIcLazy.new
  assert_nil o.get_v
  o.set_v(42)
  assert_equal 42, o.get_v
  assert_nil IvIcLazy.new.get_v  # fresh (empty) table through the same site
end

assert('global variable slots stay correct as the table grows') do
  $iv_ic_g1 = 10
  sum = 0
  i = 0
  while i < 10
    sum += $iv_ic_g1
    # defining new globals mid-loop grows/moves the table
    $iv_ic_g2 = i if i == 3
    $iv_ic_g3 = i if i == 6
    i += 1
  end
  assert_equal 100, sum
  assert_equal 3, $iv_ic_g2
  assert_equal 6, $iv_ic_g3
end
