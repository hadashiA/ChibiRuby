# Sample script for trying the ChibiRuby DAP debugger.
# Open this file in VSCode (with the chibiruby-debugger extension installed),
# press F5, and you should pause at the binding.break call.

greeting = "hello"
counter = 0

3.times do |i|
  counter += i
end

binding.break

# In the VSCode Debug Console, try:
#   greeting.upcase
#   counter * 10
#   self.class.to_s
# Locals (`greeting`, `counter`, `i`) appear in the Variables pane.

counter
