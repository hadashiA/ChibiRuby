# Sample mruby script for poking at the embedded-host debugger.
#
# The host (Program.cs) loads this file via MRubyCompiler.LoadSourceCode. When the
# binding.irb line is reached, execution suspends until a DAP client attaches to the
# host's listening port (default 4711) and sends a `continue`.

class Hero
  attr_accessor :name, :hp

  def initialize(name, hp)
    @name = name
    @hp = hp
  end

  def take_damage(amount)
    @hp -= amount
  end
end

hero = Hero.new("Alice", 100)
weapon = "Sword of Debugging"
inventory = ["potion", "map"]

hero.take_damage(15)

p "Before binding.irb: #{hero.name}, HP=#{hero.hp}"
binding.irb
# Try in the Debug Console:
#   self                                              # the toplevel object
#   binding.local_variables                            # [:hero, :weapon, :inventory]
#   binding.local_variable_get(:weapon).upcase
#   binding.local_variable_get(:hero).hp
p "After binding.irb: #{hero.name}, HP=#{hero.hp}"

inventory
