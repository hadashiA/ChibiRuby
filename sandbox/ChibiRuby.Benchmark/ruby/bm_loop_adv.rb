class Adv
  # arg actually Float -> entry IsFixnum guard must deopt, result still correct
  def divloop(s)
    x = 0; sum = 0.0
    while x < 5
      sum = sum + (2.0 * x / s)
      x += 1
    end
    sum
  end
  # polymorphic loop var (sometimes Float, sometimes Fixnum) -> must stay boxed/correct
  def poly(n)
    i = 0; acc = 0.0
    while i < n
      v = (i % 2 == 0) ? 1.5 : 3
      acc = acc + v
      i += 1
    end
    acc
  end
  # int accumulator + float reads mixed
  def mix(n)
    i = 0; s = 0.0
    while i <= n
      s = s + i * 0.5 - 1
      i += 1
    end
    s
  end
  # non-numeric touched in loop (string build) alongside numeric
  def strnum(n)
    i = 0; t = 0
    while i < n
      str = "x" * 1
      t = t + str.length + i
      i += 1
    end
    t
  end
end
a = Adv.new
[a.divloop(3), a.divloop(2.0), a.poly(6), a.mix(10), a.strnum(20)]
