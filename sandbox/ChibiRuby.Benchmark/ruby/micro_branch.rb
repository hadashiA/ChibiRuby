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
  def vdot(b)
    @x * b.x + @y * b.y + @z * b.z
  end
end

class Driver
  def run(a, b, flag)
    rs = a.vsub(b)        # rs virtual, created before the branch
    x = rs.vdot(b)        # use rs before branch
    if flag > 0.0         # branch does NOT touch rs
      x = x + 1.0
    end
    x + rs.vdot(rs)       # use rs after the branch (rs lives across it)
  end
end

a = Vec.new(1.5, 2.5, 3.5)
b = Vec.new(4.5, 5.5, 6.5)
d = Driver.new
acc = 0.0
i = 0
while i < 2000000
  acc = d.run(a, b, -1.0)
  i = i + 1
end
acc
