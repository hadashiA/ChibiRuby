#!/usr/bin/env ruby
# Regenerate lib.rbs from lib.rb via rbs-inline.
#
# Prerequisites:
#   gem install rbs-inline
#
# Usage:
#   ruby src/MRubyCS/StdLib/regen-lib-rbs.rb
#
# Why a wrapper rather than calling `rbs-inline` directly:
#   - strips the `# Generated from ... with RBS::Inline` banner
#   - removes the redundant `# : SIG` comment lines that rbs-inline emits
#     above each `def NAME: SIG` (they would otherwise pollute every
#     downstream sig/*.rbs file via the SourceGenerator).

require "open3"

HERE   = File.dirname(__FILE__)
LIB_RB = File.join(HERE, "lib.rb")
LIB_RBS = File.join(HERE, "lib.rbs")

out, status = Open3.capture2("rbs-inline", "--base=#{HERE}", LIB_RB)
unless status.success?
  warn "rbs-inline failed"
  exit 1
end

lines = out.lines

# Drop the leading "# Generated from ..." banner and the blank line after it.
if lines.first&.start_with?("# Generated from ")
  lines.shift
  lines.shift if lines.first&.strip == ""
end

# Drop `# : SIG` lines that are immediately followed by `def NAME: SIG`
# (possibly with `private`/`protected`/`public` prefix, possibly with blank
# lines between).
cleaned = []
i = 0
while i < lines.length
  line = lines[i]
  if line =~ /^(\s*)# :\s/
    j = i + 1
    j += 1 while j < lines.length && lines[j].strip.empty?
    if lines[j]&.match?(/^\s*(?:private\s+|protected\s+|public\s+)?def\s/)
      i += 1
      next
    end
  end
  cleaned << line
  i += 1
end

File.write(LIB_RBS, cleaned.join)
puts "wrote #{LIB_RBS} (#{cleaned.size} lines)"
