##
# Regexp ISO Test
# Based on mruby-onig-regexp test and regexp.md specification

#####################################
# Regexp Constants
#####################################

assert('Regexp::IGNORECASE') do
  assert_equal 1, Regexp::IGNORECASE
end

assert('Regexp::EXTENDED') do
  assert_equal 2, Regexp::EXTENDED
end

assert('Regexp::MULTILINE') do
  assert_equal 4, Regexp::MULTILINE
end

#####################################
# Regexp Class Methods
#####################################

assert('Regexp.new', '15.2.15.6.1') do
  assert_equal Regexp, Regexp.new('.*').class
  assert_equal Regexp, Regexp.new('.*', Regexp::MULTILINE).class
  assert_equal Regexp, Regexp.new('.*', Regexp::IGNORECASE).class
end

assert('Regexp.compile', '15.2.15.6.2') do
  assert_equal Regexp.compile('.*'), Regexp.new('.*')
  assert_equal Regexp.compile('test', Regexp::IGNORECASE), Regexp.new('test', Regexp::IGNORECASE)
end

assert('Regexp.escape', '15.2.15.6.3') do
  assert_equal '\\.\\*\\+\\?\\^\\$\\{\\}\\[\\]\\(\\)\\|\\\\', Regexp.escape('.*+?^${}[]()|\\')
  assert_equal 'hello\\ world', Regexp.escape('hello world')
  assert_equal 'a\\nb', Regexp.escape("a\nb")
end

assert('Regexp.quote', '15.2.15.6.4') do
  # quote is alias for escape
  assert_equal Regexp.escape('.*'), Regexp.quote('.*')
  assert_equal Regexp.escape('hello world'), Regexp.quote('hello world')
end

assert('Regexp.union') do
  re = Regexp.union('a', 'b', 'c')
  assert_not_nil re =~ 'a'
  assert_not_nil re =~ 'b'
  assert_not_nil re =~ 'c'
  assert_nil re =~ 'd'

  re = Regexp.union(/abc/i, /def/)
  assert_not_nil re =~ 'ABC'
  assert_not_nil re =~ 'def'
  assert_nil re =~ 'DEF'

  re = Regexp.union([/a/, /b/])
  assert_not_nil re =~ 'a'
  assert_not_nil re =~ 'b'
end

assert('Regexp.try_convert') do
  assert_equal Regexp, Regexp.try_convert(/abc/).class
  assert_nil Regexp.try_convert('abc')
  assert_nil Regexp.try_convert(123)
end

assert('Regexp.last_match') do
  /(\w+)/ =~ 'hello world'
  assert_equal MatchData, Regexp.last_match.class
  assert_equal 'hello', Regexp.last_match[0]
  assert_equal 'hello', Regexp.last_match(0)
  assert_equal 'hello', Regexp.last_match(1)

  /xyz/ =~ 'hello'
  assert_nil Regexp.last_match
end

#####################################
# Regexp Instance Methods
#####################################

assert('Regexp#==', '15.2.15.7.1') do
  reg1 = Regexp.new('abc')
  reg2 = Regexp.new('abc')
  reg3 = Regexp.new('def')
  reg4 = Regexp.new('abc', Regexp::IGNORECASE)

  assert_true reg1 == reg2
  assert_false reg1 == reg3
  assert_false reg1 == reg4
  assert_false reg1 == 'abc'
end

assert('Regexp#===', '15.2.15.7.2') do
  reg = Regexp.new('hello')
  assert_true reg === 'hello world'
  assert_false reg === 'goodbye'
end

assert('Regexp#=~', '15.2.15.7.3') do
  assert_equal 0, /hello/ =~ 'hello world'
  assert_equal 6, /world/ =~ 'hello world'
  assert_nil /xyz/ =~ 'hello world'
end

assert('Regexp#casefold?', '15.2.15.7.4') do
  assert_false Regexp.new('abc').casefold?
  assert_true Regexp.new('abc', Regexp::IGNORECASE).casefold?
  assert_false Regexp.new('abc', Regexp::MULTILINE).casefold?
  assert_true Regexp.new('abc', Regexp::IGNORECASE | Regexp::MULTILINE).casefold?
end

assert('Regexp#match', '15.2.15.7.5') do
  reg = Regexp.new('(\w+)')
  m = reg.match('hello world')
  assert_equal MatchData, m.class
  assert_equal 'hello', m[0]
  assert_equal 'hello', m[1]

  assert_nil reg.match('   ')
end

assert('Regexp#match with position') do
  reg = Regexp.new('def')
  assert_equal 'def', reg.match('abcdef', 3)[0]
  assert_nil reg.match('abcdef', 4)
end

assert('Regexp#match?') do
  assert_true /hello/.match?('hello world')
  assert_false /xyz/.match?('hello world')

  reg = Regexp.new('def')
  assert_true reg.match?('abcdef', 3)
  assert_false reg.match?('abcdef', 4)
end

assert('Regexp#source', '15.2.15.7.6') do
  pattern = '(\w+)\s+(\w+)'
  reg = Regexp.new(pattern)
  assert_equal pattern, reg.source
end

assert('Regexp#options') do
  assert_equal 0, Regexp.new('abc').options & 7
  assert_equal Regexp::IGNORECASE, Regexp.new('abc', Regexp::IGNORECASE).options & Regexp::IGNORECASE
  assert_equal Regexp::MULTILINE, Regexp.new('abc', Regexp::MULTILINE).options & Regexp::MULTILINE
end

assert('Regexp#named_captures') do
  reg = Regexp.new('(?<first>\w+)\s+(?<second>\w+)')
  expected = {'first' => [1], 'second' => [2]}
  assert_equal expected, reg.named_captures

  reg = Regexp.new('(\w+)')
  assert_equal({}, reg.named_captures)
end

assert('Regexp#names') do
  reg = Regexp.new('(?<first>\w+)\s+(?<second>\w+)')
  assert_equal ['first', 'second'], reg.names

  reg = Regexp.new('(\w+)')
  assert_equal [], reg.names
end

assert('Regexp#to_s') do
  reg = Regexp.new('abc')
  assert_true reg.to_s.is_a?(String)
  assert_true reg.to_s.include?('abc')
end

assert('Regexp#inspect') do
  reg = Regexp.new('abc')
  assert_equal '/abc/', reg.inspect

  reg = Regexp.new('abc', Regexp::IGNORECASE)
  assert_equal '/abc/i', reg.inspect

  reg = Regexp.new('abc', Regexp::MULTILINE)
  assert_equal '/abc/m', reg.inspect

  reg = Regexp.new('abc', Regexp::IGNORECASE | Regexp::MULTILINE)
  assert_true reg.inspect.include?('/abc/')
end

assert('Regexp#eql?') do
  reg1 = Regexp.new('abc')
  reg2 = Regexp.new('abc')
  reg3 = Regexp.new('def')

  assert_true reg1.eql?(reg2)
  assert_false reg1.eql?(reg3)
end

assert('Regexp#hash') do
  reg1 = Regexp.new('abc')
  reg2 = Regexp.new('abc')
  reg3 = Regexp.new('def')

  assert_equal reg1.hash, reg2.hash
  # hash values may collide, but usually different patterns have different hashes
end

#####################################
# MatchData Instance Methods
#####################################

def match_data_example
  Regexp.new('(\w+)(\w)').match('+aaabb-')
end

assert('MatchData#[]', '15.2.16.3.1') do
  m = match_data_example
  assert_equal 'aaabb', m[0]
  assert_equal 'aaab', m[1]
  assert_equal 'b', m[2]
  assert_nil m[3]
end

assert('MatchData#[] with negative index') do
  m = match_data_example
  assert_equal 'b', m[-1]
  assert_equal 'aaab', m[-2]
  assert_equal 'aaabb', m[-3]
end

assert('MatchData#[] with named capture') do
  m = Regexp.new('(?<name>\w+)').match('hello')
  assert_equal 'hello', m[:name]
  assert_equal 'hello', m['name']
end

assert('MatchData#[] with range') do
  m = Regexp.new('(\w)(\w)(\w)(\w)').match('abcd')
  assert_equal ['a', 'b', 'c', 'd'], m[1..-1]
  assert_equal ['a', 'b'], m[1..2]
end

assert('MatchData#begin', '15.2.16.3.2') do
  m = match_data_example
  assert_equal 1, m.begin(0)
  assert_equal 1, m.begin(1)
  assert_equal 5, m.begin(2)
  assert_raise(IndexError) { m.begin(3) }
end

assert('MatchData#end', '15.2.16.3.3') do
  m = match_data_example
  assert_equal 6, m.end(0)
  assert_equal 5, m.end(1)
  assert_equal 6, m.end(2)
  assert_raise(IndexError) { m.end(3) }
end

assert('MatchData#offset') do
  m = match_data_example
  assert_equal [1, 6], m.offset(0)
  assert_equal [1, 5], m.offset(1)
  assert_equal [5, 6], m.offset(2)
end

assert('MatchData#captures', '15.2.16.3.4') do
  m = match_data_example
  assert_equal ['aaab', 'b'], m.captures
end

assert('MatchData#captures with nil') do
  m = Regexp.new('(\w+)(\d)?').match('+aaabb-')
  assert_equal ['aaabb', nil], m.captures
end

assert('MatchData#to_a', '15.2.16.3.5') do
  m = match_data_example
  assert_equal ['aaabb', 'aaab', 'b'], m.to_a
end

assert('MatchData#to_s', '15.2.16.3.6') do
  m = match_data_example
  assert_equal 'aaabb', m.to_s
end

assert('MatchData#size', '15.2.16.3.7') do
  m = match_data_example
  assert_equal 3, m.size
end

assert('MatchData#length', '15.2.16.3.8') do
  m = match_data_example
  assert_equal 3, m.length
  assert_equal m.size, m.length
end

assert('MatchData#pre_match', '15.2.16.3.9') do
  m = match_data_example
  assert_equal '+', m.pre_match
end

assert('MatchData#post_match', '15.2.16.3.10') do
  m = match_data_example
  assert_equal '-', m.post_match
end

assert('MatchData#regexp') do
  m = match_data_example
  assert_equal '(\w+)(\w)', m.regexp.source
end

assert('MatchData#string') do
  m = match_data_example
  assert_equal '+aaabb-', m.string
  assert_true m.string.frozen?
end

assert('MatchData#named_captures') do
  m = Regexp.new('(?<a>.)(?<b>.)').match('01')
  assert_equal({'a' => '0', 'b' => '1'}, m.named_captures)
end

assert('MatchData#names') do
  m = Regexp.new('(?<a>.)(?<b>.)(?<c>.)').match('abc')
  assert_equal ['a', 'b', 'c'], m.names
end

assert('MatchData#values_at') do
  m = Regexp.new('(\w)(\w)(\w)(\w)').match('abcd')
  assert_equal ['a', 'c'], m.values_at(1, 3)
  assert_equal ['abcd', 'b', 'd'], m.values_at(0, 2, 4)
end

assert('MatchData#inspect') do
  m = match_data_example
  s = m.inspect
  assert_true s.is_a?(String)
  assert_true s.include?('MatchData')
end

#####################################
# Global Variables
#####################################

assert('$~ after match') do
  /(\w+)/ =~ 'hello world'
  assert_equal MatchData, $~.class
  assert_equal 'hello', $~[0]

  /xyz/ =~ 'hello'
  assert_nil $~
end

assert('$& matched string') do
  /(\w+)/ =~ 'hello world'
  assert_equal 'hello', $&

  /xyz/ =~ 'hello'
  assert_nil $&
end

assert('$` pre_match') do
  /world/ =~ 'hello world'
  assert_equal 'hello ', $`

  /xyz/ =~ 'hello'
  assert_nil $`
end

assert("$' post_match") do
  /hello/ =~ 'hello world'
  assert_equal ' world', $'

  /xyz/ =~ 'hello'
  assert_nil $'
end

assert('$+ last capture') do
  /(\w+)\s+(\w+)/ =~ 'hello world'
  assert_equal 'world', $+

  /xyz/ =~ 'hello'
  assert_nil $+
end

assert('$1 to $9') do
  /(\w)(\w)(\w)(\w)(\w)/ =~ 'abcde'
  assert_equal 'a', $1
  assert_equal 'b', $2
  assert_equal 'c', $3
  assert_equal 'd', $4
  assert_equal 'e', $5
  assert_nil $6
  assert_nil $7
  assert_nil $8
  assert_nil $9

  /xyz/ =~ 'hello'
  assert_nil $1
  assert_nil $2
end

#####################################
# String with Regexp
#####################################

assert('String#=~') do
  assert_equal 0, 'hello world' =~ /hello/
  assert_equal 6, 'hello world' =~ /world/
  assert_nil 'hello world' =~ /xyz/
end

assert('String#match') do
  m = 'hello world'.match(/(\w+)/)
  assert_equal MatchData, m.class
  assert_equal 'hello', m[0]
  assert_equal 'hello', m[1]

  assert_nil 'hello'.match(/xyz/)
end

assert('String#match with position') do
  m = 'abcdef'.match(/def/, 3)
  assert_equal 'def', m[0]

  assert_nil 'abcdef'.match(/def/, 4)
end

assert('String#match?') do
  assert_true 'hello world'.match?(/hello/)
  assert_false 'hello world'.match?(/xyz/)
end

assert('String#split with Regexp') do
  assert_equal ['a', 'b', 'c'], 'a1b2c'.split(/\d/)
  assert_equal ['hello', 'world'], 'hello world'.split(/\s+/)
  assert_equal ['h', 'e', 'l', 'l', 'o'], 'hello'.split(//)
end

assert('String#split with Regexp and limit') do
  assert_equal ['a', 'b', 'c3d'], 'a1b2c3d'.split(/\d/, 3)
  assert_equal ['a', 'b', 'c', 'd', ''], 'a1b2c3d4'.split(/\d/, -1)
end

assert('String#split with capturing group') do
  assert_equal ['a', '1', 'b', '2', 'c'], 'a1b2c'.split(/(\d)/)
end

assert('String#sub with Regexp') do
  assert_equal 'hxllo', 'hello'.sub(/e/, 'x')
  assert_equal 'hllo', 'hello'.sub(/e/, '')
  assert_equal 'hello', 'hello'.sub(/x/, 'y')
end

assert('String#sub with replacement patterns') do
  assert_equal 'h[e]llo', 'hello'.sub(/e/, '[\0]')
  assert_equal 'h[e]llo', 'hello'.sub(/e/, '[\&]')
  assert_equal 'h[e]llo', 'hello'.sub(/(e)/, '[\1]')
end

assert('String#sub with block') do
  assert_equal 'hEllo', 'hello'.sub(/e/) { |m| m.upcase }
  # [aeiou]+ matches only 'e' since 'l' is not a vowel
  assert_equal 'hElloworld', 'helloworld'.sub(/[aeiou]+/) { |m| m.upcase }
end

assert('String#sub!') do
  s = 'hello'
  s.sub!(/e/, 'a')
  assert_equal 'hallo', s

  s = 'hello'
  result = s.sub!(/x/, 'y')
  assert_nil result
end

assert('String#gsub with Regexp') do
  assert_equal 'hxllx', 'hello'.gsub(/[eo]/, 'x')
  assert_equal 'hll', 'hello'.gsub(/[eo]/, '')
  assert_equal 'h*ll* w*rld', 'hello world'.gsub(/[aeiou]/, '*')
end

assert('String#gsub with replacement patterns') do
  assert_equal 'h[e]ll[o]', 'hello'.gsub(/([eo])/, '[\1]')
  assert_equal 'h<e>ll<o>', 'hello'.gsub(/[eo]/, '<\0>')
end

assert('String#gsub with block') do
  assert_equal 'HELLO', 'hello'.gsub(/./) { |m| m.upcase }
  assert_equal 'hEllO', 'hello'.gsub(/[eo]/) { |m| m.upcase }
end

assert('String#gsub with hash') do
  # Use explicit hash syntax for compatibility
  assert_equal 'hEllO', 'hello'.gsub(/[eo]/, {'e' => 'E', 'o' => 'O'})
  # Empty hash means no replacements found, so matches are removed
  assert_equal 'hll', 'hello'.gsub(/[eo]/, {})
end

assert('String#gsub!') do
  s = 'hello'
  s.gsub!(/[eo]/, '*')
  assert_equal 'h*ll*', s

  s = 'hello'
  result = s.gsub!(/x/, 'y')
  assert_nil result
end

assert('String#scan') do
  assert_equal ['e', 'o'], 'hello'.scan(/[eo]/)
  assert_equal ['he', 'll'], 'hello'.scan(/../)
  # (.?) is optional, so 'o' at end matches with empty second group
  assert_equal [['h', 'e'], ['l', 'l'], ['o', '']], 'hello'.scan(/(.)(.?)/)
end

assert('String#scan with block') do
  result = []
  'hello'.scan(/[eo]/) { |m| result << m }
  assert_equal ['e', 'o'], result
end

assert('String#index with Regexp') do
  assert_equal 1, 'hello'.index(/[eo]/)
  assert_equal 4, 'hello'.index(/[eo]/, 2)
  assert_nil 'hello'.index(/x/)
end

#####################################
# Pattern Matching (case/when)
#####################################

assert('Regexp in case/when') do
  result = case 'hello'
           when /^h/ then 'starts with h'
           when /o$/ then 'ends with o'
           else 'other'
           end
  assert_equal 'starts with h', result

  result = case 'world'
           when /^h/ then 'starts with h'
           when /d$/ then 'ends with d'
           else 'other'
           end
  assert_equal 'ends with d', result
end

#####################################
# Error Cases
#####################################

assert('Invalid regexp pattern') do
  assert_raise(RegexpError) { Regexp.new('[aio') }
  assert_raise(RegexpError) { Regexp.new('*') }
  assert_raise(RegexpError) { Regexp.new('?') }
end

#####################################
# Advanced Patterns
#####################################

assert('Regexp multiline mode') do
  reg = Regexp.new('.*', Regexp::MULTILINE)
  m = reg.match("hello\nworld")
  assert_equal "hello\nworld", m[0]

  reg = Regexp.new('.*')
  m = reg.match("hello\nworld")
  assert_equal "hello", m[0]
end

assert('Regexp ignorecase mode') do
  reg = Regexp.new('hello', Regexp::IGNORECASE)
  assert_not_nil reg =~ 'HELLO'
  assert_not_nil reg =~ 'Hello'
  assert_not_nil reg =~ 'hello'
end

assert('Regexp anchors') do
  assert_equal 0, /^hello/ =~ 'hello world'
  assert_nil /^world/ =~ 'hello world'

  assert_equal 6, /world$/ =~ 'hello world'
  assert_nil /hello$/ =~ 'hello world'

  assert_equal 0, /\Ahello/ =~ 'hello world'
  assert_equal 6, /world\z/ =~ 'hello world'
end

assert('Regexp word boundary') do
  assert_equal 0, /\bhello\b/ =~ 'hello world'
  assert_nil /\bhello\b/ =~ 'helloworld'
end

assert('Regexp character classes') do
  assert_equal 0, /\d+/ =~ '123abc'
  assert_equal 3, /\D+/ =~ '123abc'
  assert_equal 0, /\w+/ =~ 'hello_123'
  assert_equal 0, /\s+/ =~ '   hello'
end

assert('Regexp quantifiers') do
  assert_equal 'aaa', /a+/.match('aaa')[0]
  assert_equal 'a', /a?/.match('aaa')[0]
  assert_equal '', /a*/.match('bbb')[0]
  assert_equal 'aa', /a{2}/.match('aaa')[0]
  assert_equal 'aaa', /a{2,}/.match('aaa')[0]
  assert_equal 'aa', /a{1,2}/.match('aaa')[0]
end

assert('Regexp alternation') do
  assert_equal 0, /cat|dog/ =~ 'cat'
  assert_equal 0, /cat|dog/ =~ 'dog'
  assert_nil /cat|dog/ =~ 'bird'
end

assert('Regexp grouping') do
  m = /(ab)+/.match('ababab')
  assert_equal 'ababab', m[0]
  assert_equal 'ab', m[1]
end

assert('Regexp non-capturing group') do
  m = /(?:ab)+/.match('ababab')
  assert_equal 'ababab', m[0]
  assert_nil m[1]
end

assert('Regexp named capture') do
  m = /(?<word>\w+)/.match('hello')
  assert_equal 'hello', m[:word]
  assert_equal 'hello', m['word']
  assert_equal 'hello', m[1]
end

assert('Regexp lookahead') do
  # Positive lookahead
  m = /\w+(?=\d)/.match('hello1')
  assert_equal 'hello', m[0]

  # Negative lookahead
  m = /\w+(?!\d)/.match('hello world')
  assert_equal 'hello', m[0]
end

assert('Regexp lookbehind') do
  # Positive lookbehind
  m = /(?<=@)\w+/.match('user@example')
  assert_equal 'example', m[0]

  # Negative lookbehind
  m = /(?<!@)\w+/.match('hello@world')
  assert_equal 'hello', m[0]
end

#####################################
# Unicode Support
#####################################

assert('Regexp with Unicode') do
  assert_equal 0, /\w+/ =~ 'hello'
  m = /(.)(.)/.match('ab')
  assert_equal 'a', m[1]
  assert_equal 'b', m[2]
end

assert('Regexp with Japanese') do
  m = /(.)(.)(.)/.match('あいう')
  assert_equal 'あいう', m[0]
  assert_equal 'あ', m[1]
  assert_equal 'い', m[2]
  assert_equal 'う', m[3]
end

assert('String#split with Japanese') do
  assert_equal ['あ', 'い', 'う'], 'あ,い,う'.split(/,/)
end

assert('String#gsub with Japanese') do
  assert_equal 'xいう', 'あいう'.gsub(/あ/, 'x')
end
