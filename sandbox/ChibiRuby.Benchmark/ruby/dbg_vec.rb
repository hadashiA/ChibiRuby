class Vec
  def initialize(x, y, z); @x = x; @y = y; @z = z; end
  def x; @x; end
  def y; @y; end
  def z; @z; end
  def vdot(b); @x * b.x + @y * b.y + @z * b.z; end
  def vlength; Math.sqrt(@x * @x + @y * @y + @z * @z); end
  def vcross(b)
    Vec.new(@y * b.z - @z * b.y, @z * b.x - @x * b.z, @x * b.y - @y * b.x)
  end
end

a = Vec.new(0.3, -0.7, 1.1)
b = Vec.new(2.0, 3.5, -1.2)
acc = 0
acc = acc + (a.vdot(b) * 1e12).to_i
acc = acc + (a.vlength * 1e12).to_i
c = a.vcross(b)
acc = acc + (c.x * 1e12).to_i + (c.y * 1e12).to_i + (c.z * 1e12).to_i
acc
