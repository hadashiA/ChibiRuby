# Sample script for trying the mruby/cs DAP debugger.
# Open this file in VSCode (with the mruby-cs-debugger extension installed),
# press F5, and you should pause at the binding.irb call.

greeting = "hello"
counter = 0

3.times do |i|
  counter += i
end

binding.irb

# In the VSCode Debug Console, try:
#   greeting.upcase
#   counter * 10
#   self.class.to_s
# Locals (`greeting`, `counter`, `i`) appear in the Variables pane.

counter
