class K
  def hash
    42
  end
  def eql?(o)
    true
  end
end
k = K.new
h = { k => 1 }
i = 0
n = 1_000_000
x = 0
while i < n
  x = h[k]
  i += 1
end
x
