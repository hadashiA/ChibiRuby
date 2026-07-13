class Vec
  def initialize(x, y, z)
    @x = x
    @y = y
    @z = z
  end
  def x; @x; end
  def y; @y; end
  def z; @z; end
  def vsub(b)
    Vec.new(@x - b.x, @y - b.y, @z - b.z)
  end
  # Multi-statement with locals -> not an inline expression, so a call on a
  # virtual receiver takes the virtual-self frame path instead of inlining.
  def weighted(b)
    s = @x * b.x
    if s > 0.0
      s = s + @y * b.y
    end
    s + @z * b.z
  end
end

class Driver
  def run(a, b)
    rs = a.vsub(b)
    rs.weighted(b) + rs.weighted(rs)
  end
end

a = Vec.new(1.5, 2.5, 3.5)
b = Vec.new(4.5, 5.5, 6.5)
d = Driver.new
acc = 0.0
i = 0
while i < 4000000
  acc = d.run(a, b)
  i = i + 1
end
acc
