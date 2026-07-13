# Stack-allocation test: `p` is created in a compiled method's while-loop and passed to a
# non-inlinable method that only READS it (non-retaining) -> p should be stack-allocated.
class Pt
  def initialize(x, y)
    @x = x
    @y = y
  end
  def x; @x; end
  def y; @y; end
end

# By-REF mutated arg (A): Box is passed to a non-inlinable method that MUTATES it via setters.
# It stays stack-allocated (passed by ref); reads after the call see the mutations.
class Box
  def initialize
    @v = 1.0
  end
  def v; @v; end
  def v=(x); @v = x; end
end

# Struct-RECEIVER variant: a stack object is the receiver of a non-accessor method that reads
# self's ivars directly (`@x`) and doesn't retain/mutate self. Big body so it is NOT inlined.
class V3
  def initialize(x, y, z)
    @x = x
    @y = y
    @z = z
  end
  def blend
    a = @x * @x + @y * @y + @z * @z
    b = @x - @y + @z * @x - @y * 0.5 + @z * 0.5
    c = a + b - a * b + a * 0.25 + b * 0.75
    d = c * c - c + c * a - c * b + a * b
    e = d + d - d * 0.5 + a - b + c
    f = a * b + c * d + e * a - b * c + d * e
    a + b + c + d + e + f
  end
end

# Nested struct field: @inner is a V3 built from literals inside initialize. Reading h.inner
# yields the inner V3 as a stack value that cascades into a struct-receiver `blend` call.
class Holder
  def initialize
    @inner = V3.new(2.0, 3.0, 6.0)
  end
  def inner; @inner; end
end

# Literal-initialized fields (no ctor args) — exercises the stack-layout literal path.
class Lit
  def initialize
    @a = 10.0
    @b = 20.0
  end
  def a; @a; end
  def b; @b; end
end

class Calc
  # Big body (> 48 IR instr) so it is NOT inlined; reads q.a/q.b only (does not retain q).
  def litsum(q)
    a = q.a * q.a + q.b * q.b
    b = q.a - q.b + q.a * q.b - q.a * 0.5 + q.b * 0.5
    c = a + b - a * b + a * 0.25 + b * 0.75
    d = c * c - c + c * a - c * b + a * b
    e = d + d - d * 0.5 + a - b + c
    f = a * b + c * d + e * a - b * c + d * e
    a + b + c + d + e + f
  end

  # Big body (> 48 IR instr) so it is NOT inlined; MUTATES b via setters (b passed by ref).
  def bump(b)
    b.v = b.v + 1.0
    b.v = b.v * 2.0
    b.v = b.v - 0.5
    b.v = b.v * b.v
    b.v = b.v + 3.0
    b.v = b.v - 1.25
    b.v = b.v * 0.5
    b.v = b.v + 7.0
    b.v = b.v - 2.5
    b.v = b.v * 1.5
    b.v
  end

  # Big body (> 48 IR instr) so it is NOT inlined; reads h.inner (a nested stack V3) and calls
  # blend on it (struct-receiver via the nested-read cascade). Does not retain h.
  def innerwork(h)
    a = h.inner.blend
    b = a * 0.5 + a - a * 0.25 + a * 2.0 - a * 3.0
    c = b + a - b * 0.5 + a * 0.75 - b * 1.5
    d = c * c - c + c * a - c * b + a * b
    e = d + d - d * 0.5 + a - b + c
    a + b + c + d + e
  end

  # Big body (> 48 IR instr) so it is NOT inlined; reads p.x/p.y only (does not retain p).
  def dist2(p)
    a = p.x * p.x + p.y * p.y
    b = p.x - p.y + p.x * p.y - p.x * 0.5 + p.y * 0.5
    c = a + b - a * b + a * 0.25 + b * 0.75
    d = c * c - c + c * a - c * b + a * b
    e = d + d - d * 0.5 + a - b + c
    f = a * b + c * d + e * a - b * c + d * e
    g = f * 0.5 + a - b * c + d - e * f
    h = g * g - f * 0.25 + a * b - c * d + e
    i = h + g - f + e - d + c - b + a * h
    j = i * 0.5 + h * 0.25 + g * 0.125 + f - e
    k = j * j - i + h * g - f * e + d * c
    l = k + j - i * 0.5 + h - g * 0.75 + f
    a + b + c + d + e + f + g + h + i + j + k + l
  end
end

class Runner
  # Loop-free (the AOT compiler bails on backward branches): create objects and pass them to a
  # non-inlinable reader. Exercises the stack-allocation struct/variant/reify path.
  def run
    calc = Calc.new
    p1 = Pt.new(1.0, 2.0)
    p2 = Pt.new(3.0, 4.0)
    p3 = Pt.new(5.0, 6.0)
    l1 = Lit.new
    l2 = Lit.new
    v1 = V3.new(1.0, 2.0, 3.0)
    v2 = V3.new(4.0, 5.0, 6.0)
    h1 = Holder.new
    h2 = Holder.new
    b1 = Box.new
    b2 = Box.new
    r1 = calc.bump(b1)
    r2 = calc.bump(b2)
    calc.dist2(p1) + calc.dist2(p2) + calc.dist2(p3) + calc.litsum(l1) + calc.litsum(l2) +
      v1.blend + v2.blend + calc.innerwork(h1) + calc.innerwork(h2) + r1 + r2 + b1.v + b2.v
  end
end

Runner.new.run
