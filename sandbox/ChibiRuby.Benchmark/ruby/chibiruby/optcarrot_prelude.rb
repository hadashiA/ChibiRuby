# Compatibility shims used only by the optcarrot benchmark.

unless Object.const_defined?(:RUBY_ENGINE)
  RUBY_ENGINE = "chibiruby"
end

unless Object.const_defined?(:RUBY_VERSION)
  RUBY_VERSION = "3.0.0"
end

module Kernel
  def puts(value = nil)
    print(value.to_s) unless value.nil?
    print("\n")
    nil
  end unless method_defined?(:puts)
end

unless Object.const_defined?(:Struct)
  class Struct
    def self.new(*members)
      klass = Class.new do
        members.each do |member|
          attr_accessor member
        end

        define_method(:initialize) do |v0 = nil, v1 = nil, v2 = nil, v3 = nil, v4 = nil, v5 = nil|
          values = [v0, v1, v2, v3, v4, v5]
          members.each_with_index do |member, i|
            instance_variable_set(:"@#{member}", values[i])
          end
        end

        define_method(:[]) do |member|
          member = members[member] if member.is_a?(Integer)
          instance_variable_get(:"@#{member}")
        end
      end

      klass.instance_eval do
        define_method(:[]) do |v0 = nil, v1 = nil, v2 = nil, v3 = nil, v4 = nil, v5 = nil|
          new(v0, v1, v2, v3, v4, v5)
        end
      end

      klass
    end
  end
end

unless Object.const_defined?(:Process)
  module Process
    CLOCK_MONOTONIC = 1

    def self.clock_gettime(_clock_id)
      Time.now.to_f
    end
  end
end

class File
  class << self
    alias binread read unless respond_to?(:binread)

    def binwrite(path, content)
      write(path, content)
    end unless respond_to?(:binwrite)

    def readable?(path)
      exist?(path)
    end unless respond_to?(:readable?)

    def basename(path)
      path = path.to_s
      i = path.size - 1
      while i >= 0
        return path[i + 1, path.size - i - 1] if path[i] == "/"
        i -= 1
      end
      path
    end unless respond_to?(:basename)

    def extname(path)
      base = basename(path)
      i = base.size - 1
      while i >= 0
        return base[i, base.size - i] if base[i] == "."
        i -= 1
      end
      ""
    end unless respond_to?(:extname)
  end
end

class Integer
  def [](index)
    (self >> index) & 1
  end unless 1.respond_to?(:[])

  def even?
    self % 2 == 0
  end unless 1.respond_to?(:even?)

  def step(limit, step = 1, &block)
    return to_enum(:step, limit, step) unless block
    raise ArgumentError, "step can't be 0" if step == 0

    i = self
    if step > 0
      while i <= limit
        block.call(i)
        i += step
      end
    else
      while i >= limit
        block.call(i)
        i += step
      end
    end
    self
  end unless 1.respond_to?(:step)
end

class String
  def b
    self
  end unless "".respond_to?(:b)

  def start_with?(*prefixes)
    prefixes.each do |prefix|
      prefix = prefix.to_s
      return true if self[0, prefix.size] == prefix
    end
    false
  end unless "".respond_to?(:start_with?)

  def chars
    ary = []
    i = 0
    while i < size
      ary << self[i]
      i += 1
    end
    ary
  end unless "".respond_to?(:chars)

  def tr(from, to)
    result = dup
    i = 0
    while i < result.size
      idx = from.index(result[i])
      result[i] = to[idx] || to[-1] if idx
      i += 1
    end
    result
  end unless "".respond_to?(:tr)

  def %(values)
    values = [values] unless values.is_a?(Array)
    result = dup
    values.each do |value|
      result = result.sub(/%[-+0-9.]*[A-Za-z]/, value.to_s)
    end
    result
  end unless "".respond_to?(:%)
end

class Array
  def slice!(index, length = nil)
    if length
      result = []
      i = 0
      while i < length && index + i < size
        result << self[index + i]
        i += 1
      end
      self[index, length] = []
      result
    else
      delete_at(index)
    end
  end unless [].respond_to?(:slice!)

  def rotate!(count = 1)
    return self if empty?
    count %= size
    count.times { self << shift }
    self
  end unless [].respond_to?(:rotate!)

  def fill(value)
    i = 0
    while i < size
      self[i] = value
      i += 1
    end
    self
  end unless [].respond_to?(:fill)

  def transpose
    return [] if empty?
    width = self[0].size
    result = []
    i = 0
    while i < width
      row = []
      j = 0
      while j < size
        row << self[j][i]
        j += 1
      end
      result << row
      i += 1
    end
    result
  end unless [].respond_to?(:transpose)

  def flatten
    result = []
    each do |value|
      if value.is_a?(Array)
        result.concat(value.flatten)
      else
        result << value
      end
    end
    result
  end unless [].respond_to?(:flatten)

  def uniq!
    seen = []
    i = 0
    while i < size
      if seen.include?(self[i])
        delete_at(i)
      else
        seen << self[i]
        i += 1
      end
    end
    self
  end unless [].respond_to?(:uniq!)
end

class Hash
  def compare_by_identity
    self
  end unless {}.respond_to?(:compare_by_identity)

  def fetch(key, default_value = nil)
    return self[key] if key?(key)
    return default_value unless default_value.nil?
    raise KeyError, "key not found: #{key.inspect}"
  end unless {}.respond_to?(:fetch)
end
