class M
  def calc(a, b, c)
    a * b + c * a - b * c + a * a + b * b + a * c
  end
end

m = M.new
acc = 0.0
i = 0
while i < 4000000
  acc = m.calc(1.5, 2.5, 3.5)
  i = i + 1
end
acc
