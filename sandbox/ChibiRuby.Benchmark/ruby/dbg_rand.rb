module Rand
  @x = 123456789
  @y = 362436069
  @z = 521288629
  @w = 88675123
  BNUM = 1 << 29
  BNUMF = BNUM.to_f
  def self.rand
    x = @x
    t = x ^ ((x & 0xfffff) << 11)
    w = @w
    @x, @y, @z = @y, @z, w
    w = @w = (w ^ (w >> 19) ^ (t ^ (t >> 8)))
    (w % BNUM) / BNUMF
  end
end

acc = 0
i = 0
while i < 1000
  acc = acc + (Rand::rand * 1e9).to_i
  i = i + 1
end
acc
