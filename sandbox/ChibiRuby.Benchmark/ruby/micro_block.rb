# Single-level times-block inline micro: sum_to news no objects but captures `s` (written by
# the block via an upvar). Interp and AOT must agree, and AOT should run the block as a C# loop.
def sum_to(n)
  s = 0
  n.times { |i| s = s + i }
  s
end

acc = 0
i = 0
while i < 200000
  acc = sum_to(100)
  i = i + 1
end
acc
