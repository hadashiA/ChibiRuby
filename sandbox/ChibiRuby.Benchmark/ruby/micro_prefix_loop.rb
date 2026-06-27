def prefix_loop(n)
  i = 0
  acc = 0
  while i < n
    acc += i
    i += 1
  end
  acc
end

prefix_loop(200000)
