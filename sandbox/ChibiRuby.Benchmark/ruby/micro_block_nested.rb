# Nested times-block inline: the inner block writes `s` (a method local, depth-1 upvar) and
# reads `j` (the outer block's param, depth-0). Exercises C2 cell pass-through.
def grid(n)
  s = 0
  n.times do |i|
    n.times do |j|
      s = s + i * j
    end
  end
  s
end

acc = 0
k = 0
while k < 100000
  acc = grid(20)
  k = k + 1
end
acc
