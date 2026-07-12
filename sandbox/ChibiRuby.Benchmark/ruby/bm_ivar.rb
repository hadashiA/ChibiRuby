# ivar-access microbenchmark: an object with many instance variables
# (like optcarrot's PPU, which has ~79) where the hot ones sit late in
# insertion order — worst case for a linear key scan.
class IvarHeavy
  def initialize
    @pad0 = 0
    @pad1 = 1
    @pad2 = 2
    @pad3 = 3
    @pad4 = 4
    @pad5 = 5
    @pad6 = 6
    @pad7 = 7
    @pad8 = 8
    @pad9 = 9
    @pad10 = 10
    @pad11 = 11
    @pad12 = 12
    @pad13 = 13
    @pad14 = 14
    @pad15 = 15
    @pad16 = 16
    @pad17 = 17
    @pad18 = 18
    @pad19 = 19
    @pad20 = 20
    @pad21 = 21
    @pad22 = 22
    @pad23 = 23
    @pad24 = 24
    @pad25 = 25
    @pad26 = 26
    @pad27 = 27
    @pad28 = 28
    @pad29 = 29
    @pad30 = 30
    @pad31 = 31
    @pad32 = 32
    @pad33 = 33
    @pad34 = 34
    @pad35 = 35
    @pad36 = 36
    @pad37 = 37
    @pad38 = 38
    @pad39 = 39
    @pad40 = 40
    @pad41 = 41
    @pad42 = 42
    @pad43 = 43
    @pad44 = 44
    @pad45 = 45
    @pad46 = 46
    @pad47 = 47
    @pad48 = 48
    @pad49 = 49
    @pad50 = 50
    @pad51 = 51
    @pad52 = 52
    @pad53 = 53
    @pad54 = 54
    @pad55 = 55
    @pad56 = 56
    @pad57 = 57
    @pad58 = 58
    @pad59 = 59
    @x = 0
    @y = 1
    @z = 2
  end

  def step(n)
    i = 0
    while i < n
      @x = @x + @y + @z
      @y = @z + i
      @z = @x - @y
      @x = @x & 0xffff
      i += 1
    end
    @x
  end
end

h = IvarHeavy.new
h.step(400_000)
