h = { foo: 1 }
i = 0
n = 1_000_000
x = 0
while i < n
  x = h[:foo]
  i += 1
end
x
