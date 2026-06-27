# Scalar-replacement micro-benchmark.
# d2 news a Vec2 temporary that never escapes -> the AOT codegen must scalar-replace it
# (zero allocation, accessor sends become field-local reads). Interpreted allocates one
# Vec2 per call; AOT should allocate ~nothing. The hot while-loop runs interpreted and
# just dispatches into the compiled d2.

class Vec2
  def initialize(x, y); @x = x; @y = y; end
  def x; @x; end
  def y; @y; end
end

class Calc
  def d2(px, py)
    v = Vec2.new(px - 0.5, py - 0.25)
    v.x * v.x + v.y * v.y
  end
end

c = Calc.new
sum = 0.0
i = 0
while i < 3000000
  sum = sum + c.d2(1.5, 2.0)
  i = i + 1
end
sum
