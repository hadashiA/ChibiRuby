# Synthetic while-loop AOT test. Exercises backward branches (while), nested loops,
# break out of a loop, and mixed int arithmetic in a *method* (so it is AOT-compiled and
# invoked via dispatch). Returns a checksum the harness compares between interp / AOT-on / AOT-off.
class WhileBench
  # Single counter loop with an accumulator.
  def sum_to(n)
    i = 0
    acc = 0
    while i < n
      acc = (acc + i * 3 - 1) & 0x3fffffff
      i += 1
    end
    acc
  end

  # Nested loops with an early break in the inner loop.
  def grid(n)
    y = 0
    total = 0
    while y < n
      x = 0
      while x < n
        v = x * y + y - x
        if v > 500
          break
        end
        total = (total + v) & 0x3fffffff
        x += 1
      end
      y += 1
    end
    total
  end

  # until-style loop (post-decrement) to cover the other backward-branch shape.
  def countdown(n)
    acc = 0
    until n <= 0
      acc = (acc * 31 + n) & 0x3fffffff
      n -= 1
    end
    acc
  end

  def run
    s = 0
    s = (s + sum_to(1000)) & 0x3fffffff
    s = (s + grid(60)) & 0x3fffffff
    s = (s + countdown(2000)) & 0x3fffffff
    s
  end
end

WhileBench.new.run
