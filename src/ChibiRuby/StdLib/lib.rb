# NOTE: Methods containing loop must be implemented in .rb(bytecode) to support jumps by break.

##
# Root of the Ruby class hierarchy. Has no superclass and an intentionally
# minimal API -- used as a base for proxy objects that must avoid inheriting
# Object/Kernel methods. Most code should subclass Object instead.
class BasicObject
  ##
  # call-seq:
  #   obj != other -> true or false
  #
  # Returns true if +self+ is not equal to +other+. Defined as the
  # negation of <code>==</code>; subclasses typically only need to
  # override <code>==</code>.
  def !=(other)
    if self == other
      false
    else
      true
    end
  end
end

##
# Mixin included by Object that provides the methods available on every
# object -- puts, raise, require, send, respond_to?, and so on. Top-level
# def's become private Kernel methods, which is why they can be called
# anywhere without an explicit receiver.
module Kernel
  ##
  # call-seq:
  #    obj.extend(module, ...)    -> obj
  #
  # Adds to _obj_ the instance methods from each module given as a
  # parameter.
  #
  #    module Mod
  #      def hello
  #        "Hello from Mod.\n"
  #      end
  #    end
  #
  #    class Klass
  #      def hello
  #        "Hello from Klass.\n"
  #      end
  #    end
  #
  #    k = Klass.new
  #    k.hello         #=> "Hello from Klass.\n"
  #    k.extend(Mod)   #=> #<Klass:0x401b3bc8>
  #    k.hello         #=> "Hello from Mod.\n"
  #
  # ISO 15.3.1.3.13
  def extend(*args)
    args.reverse!
    obj = self
    args.each do |m|
      m.__send__(:extend_object, obj)
      m.__send__(:extended, obj)
    end
    self
  end

  # 15.3.1.2.1 Kernel.`
  # provided by Kernel#`
  # 15.3.1.3.3
  def `(s)
    raise NotImplementedError.new("backquotes not implemented")
  end

  ##
  # 15.3.1.2.3  Kernel.eval
  # 15.3.1.3.12 Kernel#eval
  # NotImplemented by mruby core; use mruby-eval gem

  ##
  # ISO 15.3.1.2.8 Kernel.loop
  # not provided by mruby

  ##
  # Calls the given block repetitively.
  #
  # ISO 15.3.1.3.29
  private def loop(&block)
    return to_enum :loop unless block

    while true
      yield
    end
  rescue StopIteration => e
    e.result
  end

  # 11.4.4 Step c)
  def !~(y)
    !(self =~ y)
  end

  # call-seq:
  #   obj.yield_self {|_obj|...} -> an_object
  #   obj.then {|_obj|...}       -> an_object
  #
  # Yields *obj* and returns the result.
  #
  #   'my string'.yield_self {|s|s.upcase} #=> "MY STRING"
  #
  def yield_self(&block)
    return to_enum :yield_self unless block
    block.call(self)
  end
  alias then yield_self

  ##
  #  call-seq:
  #     obj.tap{|x|...}    -> obj
  #
  #  Yields `x` to the block, and then returns `x`.
  #  The primary purpose of this method is to "tap into" a method chain,
  #  in order to perform operations on intermediate results within the chain.
  #
  #  (1..10)                .tap {|x| puts "original: #{x.inspect}"}
  #    .to_a                .tap {|x| puts "array: #{x.inspect}"}
  #    .select {|x| x%2==0} .tap {|x| puts "evens: #{x.inspect}"}
  #    .map { |x| x*x }     .tap {|x| puts "squares: #{x.inspect}"}
  #
  def tap
    yield self
    self
  end
end

##
# Raised when a name (constant, variable, or method) cannot be resolved.
# The offending +name+ is available via the +#name+ accessor.
#
# ISO 15.2.31
class NameError < StandardError
  ##
  # The undefined name that triggered the error, as a Symbol.
  attr_accessor :name

  ##
  # call-seq:
  #   NameError.new(message=nil, name=nil) -> name_error
  #
  # Constructs a new NameError with the optional +message+ and the
  # offending +name+ stored for later retrieval via #name.
  def initialize(message=nil, name=nil)
    @name = name
    super(message)
  end
end

##
# Raised when a method is called on a receiver that does not respond to it.
# Subclass of NameError; adds the +#args+ accessor for the arguments that
# were passed to the missing method.
#
# ISO 15.2.32
class NoMethodError < NameError
  ##
  # The arguments passed to the undefined method, as an Array.
  attr_reader :args

  ##
  # call-seq:
  #   NoMethodError.new(message=nil, name=nil, args=nil) -> no_method_error
  #
  # Constructs a new NoMethodError with the optional +message+, the
  # offending method +name+, and the +args+ array passed to the call.
  def initialize(message=nil, name=nil, args=nil)
    @args = args
    super message, name
  end
end

class Module
  # 15.2.2.4.11
  alias attr attr_reader

  # 15.2.2.4.27
  def include(*args)
    args.reverse!
    mod = self
    args.each do |m|
      m.__send__(:append_features, mod)
      m.__send__(:included, mod)
    end
    self
  end

  ##
  # call-seq:
  #   prepend(module, ...) -> self
  #
  # Invokes Module.prepend_features on each parameter in reverse order
  # so that the prepended modules take priority over methods defined
  # in +self+.
  def prepend(*args)
    args.reverse!
    mod = self
    args.each do |m|
      m.__send__(:prepend_features, mod)
      m.__send__(:prepended, mod)
    end
    self
  end
end

##
# Enumerable
#
# The <code>Enumerable</code> mixin provides collection classes with
# several traversal and searching methods, and with the ability to
# sort. The class must provide a method `each`, which
# yields successive members of the collection. If
# {Enumerable#max}, {#min}, or
# {#sort} is used, the objects in the collection must also
# implement a meaningful `<=>` operator, as these methods
# rely on an ordering between members of the collection.
#
# ISO 15.3.2
module Enumerable

  # @!visibility private
  NONE = Object.new

  ##
  # Call the given block for each element
  # which is yield by +each+. Return false
  # if one block value is false. Otherwise
  # return true. If no block is given and
  # +self+ is false return false.
  #
  # ISO 15.3.2.2.1
  def all?(&block)
    if block
      self.each{|*val| return false unless block.call(*val)}
    else
      self.each{|*val| return false unless val.__svalue}
    end
    true
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return true
  # if one block value is true. Otherwise
  # return false. If no block is given and
  # +self+ is true object return true.
  #
  # ISO 15.3.2.2.2
  def any?(&block)
    if block
      self.each{|*val| return true if block.call(*val)}
    else
      self.each{|*val| return true if val.__svalue}
    end
    false
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Append all
  # values of each block together and
  # return this value.
  #
  # ISO 15.3.2.2.3
  def collect(&block)
    return to_enum :collect unless block

    ary = []
    self.each{|*val| ary.push(block.call(*val))}
    ary
  end

  ##
  # Return the first element for which
  # value from the block is true. If no
  # object matches, calls +ifnone+ and
  # returns its result. Otherwise returns
  # +nil+.
  #
  # ISO 15.3.2.2.4
  def detect(ifnone=nil, &block)
    return to_enum :detect, ifnone unless block

    self.each{|*val|
      if block.call(*val)
        return val.__svalue
      end
    }
    ifnone.call unless ifnone.nil?
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Pass an
  # index to the block which starts at 0
  # and increase by 1 for each element.
  #
  # ISO 15.3.2.2.5
  def each_with_index(&block)
    return to_enum :each_with_index unless block

    i = 0
    self.each{|*val|
      block.call(val.__svalue, i)
      i += 1
    }
    self
  end

  ##
  # Return an array of all elements which
  # are yield by +each+.
  #
  # ISO 15.3.2.2.6
  def entries
    ary = []
    self.each{|*val|
      # __svalue is an internal method
      ary.push val.__svalue
    }
    ary
  end

  ##
  # Alias for find
  #
  # ISO 15.3.2.2.7
  alias find detect

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an array
  # which contains all elements whose block
  # value was true.
  #
  # ISO 15.3.2.2.8
  def find_all(&block)
    return to_enum :find_all unless block

    ary = []
    self.each{|*val|
      ary.push(val.__svalue) if block.call(*val)
    }
    ary
  end

  ##
  # Call the given block for each element
  # which is yield by +each+ and which return
  # value was true when invoking === with
  # +pattern+. Return an array with all
  # elements or the respective block values.
  #
  # ISO 15.3.2.2.9
  def grep(pattern, &block)
    ary = []
    self.each{|*val|
      sv = val.__svalue
      if pattern === sv
        ary.push((block)? block.call(*val): sv)
      end
    }
    ary
  end

  ##
  # Return true if at least one element which
  # is yield by +each+ returns a true value
  # by invoking == with +obj+. Otherwise return
  # false.
  #
  # ISO 15.3.2.2.10
  def include?(obj)
    self.each{|*val|
      return true if val.__svalue == obj
    }
    false
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return value
  # is the sum of all block values. Pass
  # to each block the current sum and the
  # current element.
  #
  # ISO 15.3.2.2.11
  def inject(*args, &block)
    raise ArgumentError, "too many arguments" if args.size > 2
    if Symbol === args[-1]
      sym = args[-1]
      block = ->(x,y){x.__send__(sym,y)}
      args.pop
    end
    if args.empty?
      flag = true  # no initial argument
      result = nil
    else
      flag = false
      result = args[0]
    end
    self.each{|*val|
      val = val.__svalue
      if flag
        # push first element as initial
        flag = false
        result = val
      else
        result = block.call(result, val)
      end
    }
    result
  end
  alias reduce inject

  ##
  # Alias for collect
  #
  # ISO 15.3.2.2.12
  alias map collect

  ##
  # Return the maximum value of all elements
  # yield by +each+. If no block is given <=>
  # will be invoked to define this value. If
  # a block is given it will be used instead.
  #
  # ISO 15.3.2.2.13
  def max(&block)
    flag = true  # 1st element?
    result = nil
    self.each{|*val|
      val = val.__svalue
      if flag
        # 1st element
        result = val
        flag = false
      else
        if block
          result = val if block.call(val, result) > 0
        else
          result = val if (val <=> result) > 0
        end
      end
    }
    result
  end

  ##
  # Return the minimum value of all elements
  # yield by +each+. If no block is given <=>
  # will be invoked to define this value. If
  # a block is given it will be used instead.
  #
  # ISO 15.3.2.2.14
  def min(&block)
    flag = true  # 1st element?
    result = nil
    self.each{|*val|
      val = val.__svalue
      if flag
        # 1st element
        result = val
        flag = false
      else
        if block
          result = val if block.call(val, result) < 0
        else
          result = val if (val <=> result) < 0
        end
      end
    }
    result
  end

  ##
  # Alias for include?
  #
  # ISO 15.3.2.2.15
  alias member? include?

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an
  # array which contains two arrays. The
  # first array contains all elements
  # whose block value was true. The second
  # array contains all elements whose
  # block value was false.
  #
  # ISO 15.3.2.2.16
  def partition(&block)
    return to_enum :partition unless block

    ary_T = []
    ary_F = []
    self.each{|*val|
      if block.call(*val)
        ary_T.push(val.__svalue)
      else
        ary_F.push(val.__svalue)
      end
    }
    [ary_T, ary_F]
  end

  ##
  # Call the given block for each element
  # which is yield by +each+. Return an
  # array which contains only the elements
  # whose block value was false.
  #
  # ISO 15.3.2.2.17
  def reject(&block)
    return to_enum :reject unless block

    ary = []
    self.each{|*val|
      ary.push(val.__svalue) unless block.call(*val)
    }
    ary
  end

  ##
  # Alias for find_all.
  #
  # ISO 15.3.2.2.18
  alias select find_all

  ##
  # Return a sorted array of all elements
  # which are yield by +each+. If no block
  # is given <=> will be invoked on each
  # element to define the order. Otherwise
  # the given block will be used for
  # sorting.
  #
  # ISO 15.3.2.2.19
  def sort(&block)
    self.map{|*val| val.__svalue}.sort(&block)
  end

  ##
  # Alias for entries.
  #
  # ISO 15.3.2.2.20
  alias to_a entries

  # redefine #hash 15.3.1.3.15
  def hash
    h = 12347
    i = 0
    self.each do |e|
      h = __update_hash(h, i, e.hash)
      i += 1
    end
    h
  end
end

# Enumerable extensions (from mruby-enum-ext)
module Enumerable
  ##
  # call-seq:
  #   enum.drop(n) -> array
  #
  # Drops the first +n+ elements from +enum+ and returns the remaining
  # elements as an array.
  #
  #   [1, 2, 3, 4, 5].drop(2)   #=> [3, 4, 5]
  def drop(n)
    n = n.__to_int
    raise ArgumentError, "attempt to drop negative size" if n < 0

    ary = []
    self.each {|*val| n == 0 ? ary << val.__svalue : n -= 1 }
    ary
  end

  ##
  # call-seq:
  #   enum.drop_while {|element| ... } -> array
  #   enum.drop_while                  -> enumerator
  #
  # Drops elements up to, but not including, the first element for which
  # the block returns a falsy value and returns an array containing the
  # remaining elements.
  def drop_while(&block)
    return to_enum :drop_while unless block

    ary, state = [], false
    self.each do |*val|
      state = true if !state and !block.call(*val)
      ary << val.__svalue if state
    end
    ary
  end

  ##
  # call-seq:
  #   enum.take(n) -> array
  #
  # Returns the first +n+ elements from +enum+ as an array.
  #
  #   [1, 2, 3, 4, 5].take(3)   #=> [1, 2, 3]
  def take(n)
    n = n.__to_int
    i = n.to_i
    raise ArgumentError, "attempt to take negative size" if i < 0
    ary = []
    return ary if i == 0
    self.each do |*val|
      ary << val.__svalue
      i -= 1
      break if i == 0
    end
    ary
  end

  ##
  # call-seq:
  #   enum.take_while {|element| ... } -> array
  #   enum.take_while                  -> enumerator
  #
  # Passes elements to the block until the block returns a falsy value,
  # then stops iterating and returns an array of all prior elements.
  def take_while(&block)
    return to_enum :take_while unless block

    ary = []
    self.each do |*val|
      return ary unless block.call(*val)
      ary << val.__svalue
    end
    ary
  end

  ##
  # call-seq:
  #   enum.each_cons(n) {|array| ... } -> nil
  #   enum.each_cons(n)                -> enumerator
  #
  # Iterates over the elements in consecutive groups of +n+ items,
  # passing each group as an array to the block.
  #
  #   (1..5).each_cons(3) {|a| p a }
  #   # [1, 2, 3]
  #   # [2, 3, 4]
  #   # [3, 4, 5]
  def each_cons(n, &block)
    n = n.__to_int
    raise ArgumentError, "invalid size" if n <= 0

    return to_enum(:each_cons,n) unless block
    ary = []
    n = n.to_i
    self.each do |*val|
      ary.shift if ary.size == n
      ary << val.__svalue
      block.call(ary.dup) if ary.size == n
    end
    nil
  end

  ##
  # call-seq:
  #   enum.each_slice(n) {|array| ... } -> nil
  #   enum.each_slice(n)                -> enumerator
  #
  # Splits the enumerable into chunks of +n+ elements and yields each
  # chunk as an array to the block. The final chunk may have fewer
  # elements.
  #
  #   (1..7).each_slice(3) {|a| p a }
  #   # [1, 2, 3]
  #   # [4, 5, 6]
  #   # [7]
  def each_slice(n, &block)
    n = n.__to_int
    raise ArgumentError, "invalid slice size" if n <= 0

    return to_enum(:each_slice,n) unless block
    ary = []
    n = n.to_i
    self.each do |*val|
      ary << val.__svalue
      if ary.size == n
        block.call(ary)
        ary = []
      end
    end
    block.call(ary) unless ary.empty?
    nil
  end

  ##
  # call-seq:
  #   enum.group_by {|element| ... } -> hash
  #   enum.group_by                  -> enumerator
  #
  # Groups elements by the value returned from the block and returns a
  # hash whose keys are the block's results and whose values are arrays
  # of corresponding elements.
  def group_by(&block)
    return to_enum :group_by unless block

    h = {}
    self.each do |*val|
      key = block.call(*val)
      sv = val.__svalue
      h.key?(key) ? (h[key] << sv) : (h[key] = [sv])
    end
    h
  end

  ##
  # call-seq:
  #   enum.sort_by {|element| ... } -> array
  #   enum.sort_by                  -> enumerator
  #
  # Returns an array of elements sorted by mapping each through the
  # block and ordering by the block's result.
  def sort_by(&block)
    return to_enum :sort_by unless block
    self.to_a.sort_by(&block)
  end

  ##
  # call-seq:
  #   enum.first       -> object or nil
  #   enum.first(n)    -> array
  #
  # With no argument, returns the first element. With argument +n+,
  # returns an array of the first +n+ elements.
  def first(*args)
    case args.length
    when 0
      self.each do |*val|
        return val.__svalue
      end
      return nil
    when 1
      i = args[0].__to_int
      raise ArgumentError, "attempt to take negative size" if i < 0
      ary = []
      return ary if i == 0
      self.each do |*val|
        ary << val.__svalue
        i -= 1
        break if i == 0
      end
      ary
    else
      raise ArgumentError, "wrong number of arguments (given #{args.length}, expected 0..1)"
    end
  end

  ##
  # call-seq:
  #   enum.count                    -> integer
  #   enum.count(item)              -> integer
  #   enum.count {|element| ... }   -> integer
  #
  # Returns the number of elements. With argument +item+, returns the
  # count of elements equal to +item+. With a block, returns the count
  # of elements for which the block returns a truthy value.
  def count(v=NONE, &block)
    count = 0
    if block
      self.each do |*val|
        count += 1 if block.call(*val)
      end
    else
      if NONE.equal?(v)
        self.each { count += 1 }
      else
        self.each do |*val|
          count += 1 if val.__svalue == v
        end
      end
    end
    count
  end

  ##
  # call-seq:
  #   enum.flat_map {|element| ... }       -> array
  #   enum.flat_map                        -> enumerator
  #   enum.collect_concat {|element| ... } -> array
  #
  # Returns a new array with the concatenated results of running the
  # block once for every element. One level of array nesting in the
  # block's return value is flattened.
  def flat_map(&block)
    return to_enum :flat_map unless block

    ary = []
    self.each do |*e|
      e2 = block.call(*e)
      if e2.respond_to? :each
        e2.each {|e3| ary.push(e3) }
      else
        ary.push(e2)
      end
    end
    ary
  end
  alias collect_concat flat_map

  ##
  # call-seq:
  #   enum.max_by {|element| ... } -> object
  #   enum.max_by                  -> enumerator
  #
  # Returns the element whose block return value is greatest. Returns
  # nil if the enumerable is empty.
  def max_by(&block)
    return to_enum :max_by unless block

    first = true
    max = nil
    max_cmp = nil

    self.each do |*val|
      if first
        max = val.__svalue
        max_cmp = block.call(*val)
        first = false
      else
        if (cmp = block.call(*val)) > max_cmp
          max = val.__svalue
          max_cmp = cmp
        end
      end
    end
    max
  end

  ##
  # call-seq:
  #   enum.min_by {|element| ... } -> object
  #   enum.min_by                  -> enumerator
  #
  # Returns the element whose block return value is smallest. Returns
  # nil if the enumerable is empty.
  def min_by(&block)
    return to_enum :min_by unless block

    first = true
    min = nil
    min_cmp = nil

    self.each do |*val|
      if first
        min = val.__svalue
        min_cmp = block.call(*val)
        first = false
      else
        if (cmp = block.call(*val)) < min_cmp
          min = val.__svalue
          min_cmp = cmp
        end
      end
    end
    min
  end

  ##
  # call-seq:
  #   enum.minmax                  -> [min, max]
  #   enum.minmax {|a, b| ... }    -> [min, max]
  #
  # Returns a two-element array containing the minimum and maximum
  # elements. Uses +<=>+ by default, or the given block for comparison.
  def minmax(&block)
    max = nil
    min = nil
    first = true

    self.each do |*val|
      if first
        val = val.__svalue
        max = val
        min = val
        first = false
      else
        val = val.__svalue
        if block
          max = val if block.call(val, max) > 0
          min = val if block.call(val, min) < 0
        else
          max = val if (val <=> max) > 0
          min = val if (val <=> min) < 0
        end
      end
    end
    [min, max]
  end

  ##
  # call-seq:
  #   enum.minmax_by {|element| ... } -> [min, max]
  #   enum.minmax_by                  -> enumerator
  #
  # Returns a two-element array containing the elements with the
  # smallest and greatest block return values, respectively.
  def minmax_by(&block)
    return to_enum :minmax_by unless block

    max = nil
    max_cmp = nil
    min = nil
    min_cmp = nil
    first = true

    self.each do |*val|
      if first
        max = min = val.__svalue
        max_cmp = min_cmp = block.call(*val)
        first = false
      else
        if (cmp = block.call(*val)) > max_cmp
          max = val.__svalue
          max_cmp = cmp
        end
        if (cmp = block.call(*val)) < min_cmp
          min = val.__svalue
          min_cmp = cmp
        end
      end
    end
    [min, max]
  end

  ##
  # call-seq:
  #   enum.none?                    -> true or false
  #   enum.none?(pattern)           -> true or false
  #   enum.none? {|element| ... }   -> true or false
  #
  # Returns true if no element is truthy (or matches +pattern+ via
  # <code>===</code>, or yields truthy from the block). Returns false
  # otherwise.
  def none?(pat=NONE, &block)
    if !NONE.equal?(pat)
      self.each do |*val|
        return false if pat === val.__svalue
      end
    elsif block
      self.each do |*val|
        return false if block.call(*val)
      end
    else
      self.each do |*val|
        return false if val.__svalue
      end
    end
    true
  end

  ##
  # call-seq:
  #   enum.one?                    -> true or false
  #   enum.one?(pattern)           -> true or false
  #   enum.one? {|element| ... }   -> true or false
  #
  # Returns true if exactly one element is truthy (or matches the given
  # +pattern+ / block). Returns false otherwise.
  def one?(pat=NONE, &block)
    count = 0
    if !NONE.equal?(pat)
      self.each do |*val|
        count += 1 if pat === val.__svalue
        return false if count > 1
      end
    elsif block
      self.each do |*val|
        count += 1 if block.call(*val)
        return false if count > 1
      end
    else
      self.each do |*val|
        count += 1 if val.__svalue
        return false if count > 1
      end
    end

    count == 1 ? true : false
  end

  ##
  # call-seq:
  #   enum.all?(pattern)           -> true or false
  #   enum.all? {|element| ... }   -> true or false
  #
  # Overrides the earlier <code>all?</code> definition to add the
  # +pattern+ argument: returns true only if every element matches
  # +pattern+ via <code>===</code>. Without +pattern+, behavior matches
  # the earlier block/truthy form.
  def all?(pat=NONE, &block)
    if !NONE.equal?(pat)
      self.each{|*val| return false unless pat === val.__svalue}
    elsif block
      self.each{|*val| return false unless block.call(*val)}
    else
      self.each{|*val| return false unless val.__svalue}
    end
    true
  end

  ##
  # call-seq:
  #   enum.any?(pattern)           -> true or false
  #   enum.any? {|element| ... }   -> true or false
  #
  # Overrides the earlier <code>any?</code> definition to add the
  # +pattern+ argument: returns true if any element matches +pattern+
  # via <code>===</code>. Without +pattern+, behavior matches the
  # earlier block/truthy form.
  def any?(pat=NONE, &block)
    if !NONE.equal?(pat)
      self.each{|*val| return true if pat === val.__svalue}
    elsif block
      self.each{|*val| return true if block.call(*val)}
    else
      self.each{|*val| return true if val.__svalue}
    end
    false
  end

  ##
  # call-seq:
  #   enum.each_with_object(obj) {|element, obj| ... } -> obj
  #   enum.each_with_object(obj)                       -> enumerator
  #
  # Iterates the block with each element and the given +obj+, returning
  # +obj+ after iteration. Useful for building up a result object.
  def each_with_object(obj, &block)
    return to_enum(:each_with_object, obj) unless block

    self.each {|*val| block.call(val.__svalue, obj) }
    obj
  end

  ##
  # call-seq:
  #   enum.reverse_each {|element| ... } -> self
  #   enum.reverse_each                  -> enumerator
  #
  # Iterates the block over each element in reverse order. Internally
  # builds an array of all elements first.
  def reverse_each(&block)
    return to_enum :reverse_each unless block

    ary = self.to_a
    i = ary.size - 1
    while i>=0
      block.call(ary[i])
      i -= 1
    end
    self
  end

  ##
  # call-seq:
  #   enum.cycle(n=nil) {|element| ... } -> nil
  #   enum.cycle(n=nil)                  -> enumerator
  #
  # Calls the block for each element repeatedly +n+ times, or forever
  # if +n+ is nil. Returns nil after iteration completes (only when +n+
  # is finite).
  def cycle(nv = nil, &block)
    return to_enum(:cycle, nv) unless block

    n = nil

    if nv.nil?
      n = -1
    else
      n = nv.__to_int
      return nil if n <= 0
    end

    ary = []
    each do |*i|
      ary.push(i)
      yield(*i)
    end
    return nil if ary.empty?

    while n < 0 || 0 < (n -= 1)
      ary.each do |i|
        yield(*i)
      end
    end

    nil
  end

  ##
  # call-seq:
  #   enum.find_index(value)            -> integer or nil
  #   enum.find_index {|element| ... }  -> integer or nil
  #   enum.find_index                   -> enumerator
  #
  # Returns the zero-based index of the first element equal to +value+
  # or for which the block returns a truthy value. Returns nil if no
  # such element is found.
  def find_index(val=NONE, &block)
    return to_enum(:find_index, val) if !block && NONE.equal?(val)

    idx = 0
    if block
      self.each do |*e|
        return idx if block.call(*e)
        idx += 1
      end
    else
      self.each do |*e|
        return idx if e.__svalue == val
        idx += 1
      end
    end
    nil
  end

  ##
  # call-seq:
  #   enum.zip(other, ...)                  -> array
  #   enum.zip(other, ...) {|array| ... }   -> nil
  #
  # Combines +enum+ with +other+ enumerables, producing arrays of
  # corresponding elements. With a block, yields each combined array
  # and returns nil.
  def zip(*arg, &block)
    result = block ? nil : []
    arg = arg.map do |a|
      unless a.respond_to?(:to_a)
        raise TypeError, "wrong argument type #{a.class} (must respond to :to_a)"
      end
      a.to_a
    end

    i = 0
    self.each do |*val|
      a = []
      a.push(val.__svalue)
      idx = 0
      while idx < arg.size
        a.push(arg[idx][i])
        idx += 1
      end
      i += 1
      if result.nil?
        block.call(a)
      else
        result.push(a)
      end
    end
    result
  end

  ##
  # call-seq:
  #   enum.to_h                     -> hash
  #   enum.to_h {|element| ... }    -> hash
  #
  # Returns a hash built from the enumerable's elements. Each element
  # must be a two-element array <code>[key, value]</code>. With a block,
  # the block is called with each element and must return such a pair.
  def to_h(&blk)
    h = {}
    if blk
      self.each do |v|
        v = blk.call(v)
        raise TypeError, "wrong element type #{v.class} (expected Array)" unless v.is_a? Array
        raise ArgumentError, "element has wrong array length (expected 2, was #{v.size})" if v.size != 2
        h[v[0]] = v[1]
      end
    else
      self.each do |*v|
        v = v.__svalue
        raise TypeError, "wrong element type #{v.class} (expected Array)" unless v.is_a? Array
        raise ArgumentError, "element has wrong array length (expected 2, was #{v.size})" if v.size != 2
        h[v[0]] = v[1]
      end
    end
    h
  end

  ##
  # call-seq:
  #   enum.uniq                     -> array
  #   enum.uniq {|element| ... }    -> array
  #
  # Returns an array containing only the unique elements of +enum+.
  # With a block, two elements are considered equal when they produce
  # the same block return value.
  def uniq(&block)
    hash = {}
    if block
      self.each do|*v|
        v = v.__svalue
        hash[block.call(v)] ||= v
      end
    else
      self.each do|*v|
        v = v.__svalue
        hash[v] ||= v
      end
    end
    hash.values
  end

  ##
  # call-seq:
  #   enum.filter_map {|element| ... } -> array
  #   enum.filter_map                  -> enumerator
  #
  # Returns an array of truthy block return values. Equivalent to
  # <code>map {...}.select {|x| x }</code> but in a single pass.
  def filter_map(&blk)
    return to_enum(:filter_map) unless blk

    ary = []
    self.each do |x|
      x = blk.call(x)
      ary.push x if x
    end
    ary
  end

  alias filter select

  ##
  # call-seq:
  #   enum.grep_v(pattern)                  -> array
  #   enum.grep_v(pattern) {|element| ... } -> array
  #
  # Returns an array of elements for which <code>pattern === element</code>
  # is false. With a block, returns the block results for those elements.
  def grep_v(pattern, &block)
    ary = []
    self.each{|*val|
      sv = val.__svalue
      unless pattern === sv
        ary.push((block)? block.call(*val): sv)
      end
    }
    ary
  end

  ##
  # call-seq:
  #   enum.tally -> hash
  #
  # Counts occurrences of each element and returns a hash mapping each
  # element to its count.
  #
  #   %w(a b c b a a).tally   #=> {"a"=>3, "b"=>2, "c"=>1}
  def tally
    hash = {}
    self.each do |x|
      hash[x] = (hash[x]||0)+1
    end
    hash
  end

  ##
  # call-seq:
  #   enum.sum(init=0)                   -> number
  #   enum.sum(init=0) {|element| ... }  -> number
  #
  # Returns the sum of elements (or block return values) added to
  # +init+. Uses the <code>+</code> operator.
  def sum(init=0,&block)
    result=init
    if block
      self.each do |e|
        result += block.call(e)
      end
    else
      self.each do |e|
        result += e
      end
    end
    result
  end

  ##
  # call-seq:
  #   enum.each_entry {|element| ... } -> self
  #   enum.each_entry                  -> enumerator
  #
  # Calls the block once for each element of +self+, converting
  # multiple values from <code>each</code> into a single value via
  # <code>__svalue</code>.
  def each_entry(*args, &blk)
    return to_enum(:each_entry) unless blk
    self.each do |*a|
      yield a.__svalue
    end
    return self
  end
end

##
# Array
#
# Ordered, integer-indexed collection of objects -- the base concrete
# container in Ruby. Supports indexing, slicing, iteration via +each+,
# and conversion via +to_a+. Mutable; many in-place methods end in +!+.
#
# ISO 15.2.12
class Array
  ##
  # call-seq:
  #   array.each {|element| ... } -> self
  #   array.each -> Enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the respective element.
  #
  # ISO 15.2.12.5.10
  def each(&block)
    return to_enum :each unless block

    idx = 0
    while idx < length
      block.call(self[idx])
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.each_index {|index| ... } -> self
  #   array.each_index -> Enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the index of the respective element.
  #
  # ISO 15.2.12.5.11
  def each_index(&block)
    return to_enum :each_index unless block

    idx = 0
    while idx < length
      block.call(idx)
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.collect! {|element| ... } -> self
  #   array.collect! -> new_enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the respective element. Each element will
  # be replaced by the resulting values.
  #
  # ISO 15.2.12.5.7
  def collect!(&block)
    return to_enum :collect! unless block

    idx = 0
    len = size
    while idx < len
      self[idx] = block.call self[idx]
      idx += 1
    end
    self
  end

  ##
  # call-seq:
  #   array.map! {|element| ... } -> self
  #   array.map! -> new_enumerator
  #
  # Alias for collect!
  #
  # ISO 15.2.12.5.20
  alias map! collect!

  ##
  # Private method for Array creation.
  #
  # ISO 15.2.12.5.15
  def initialize(size=0, obj=nil, &block)
    if size.is_a?(Array) && obj==nil && block == nil
      self.replace(size)
      return self
    end
    size = size.__to_int
    raise ArgumentError, "negative array size" if size < 0

    self.clear
    if size > 0
      self[size - 1] = nil # allocate

      idx = 0
      while idx < size
        self[idx] = (block)? block.call(idx): obj
        idx += 1
      end
    end

    self
  end

  ##
  # call-seq:
  #   array == other   -> true or false
  #
  #  Equality---Two arrays are equal if they contain the same number
  #  of elements and if each element is equal to (according to
  #  Object.==) the corresponding element in the other array.
  #
  def ==(other)
    other = self.__ary_eq(other)
    return false if other == false
    return true  if other == true
    len = self.size
    i = 0
    while i < len
      return false if self[i] != other[i]
      i += 1
    end
    return true
  end

  ##
  # call-seq:
  #   array.eql? other_array -> true or false
  #
  #  Returns <code>true</code> if +self+ and _other_ are the same object,
  #  or are both arrays with the same content.
  #
  def eql?(other)
    other = self.__ary_eq(other)
    return false if other == false
    return true  if other == true
    len = self.size
    i = 0
    while i < len
      return false unless self[i].eql?(other[i])
      i += 1
    end
    return true
  end

  ##
  # call-seq:
  #   array <=> other_array -> -1, 0, or 1
  #
  #  Comparison---Returns an integer (-1, 0, or +1)
  #  if this array is less than, equal to, or greater than <i>other_ary</i>.
  #  Each object in each array is compared (using <=>). If any value isn't
  #  equal, then that inequality is the return value. If all the
  #  values found are equal, then the return is based on a
  #  comparison of the array lengths. Thus, two arrays are
  #  "equal" according to <code>Array#<=></code> if and only if they have
  #  the same length and the value of each element is equal to the
  #  value of the corresponding element in the other array.
  #
  def <=>(other)
    other = self.__ary_cmp(other)
    return 0 if 0 == other
    return nil if nil == other

    len = self.size
    n = other.size
    len = n if len > n
    i = 0
    begin
      while i < len
        n = (self[i] <=> other[i])
        return n if n.nil? || n != 0
        i += 1
      end
    rescue NoMethodError
      return nil
    end
    len = self.size - other.size
    if len == 0
      0
    elsif len > 0
      1
    else
      -1
    end
  end

  ##
  # call-seq:
  #   array.delete(obj) -> deleted_object
  #   array.delete(obj) {|nosuch| ... } -> deleted_object or block_return
  #
  # Delete element with index +key+
  def delete(key, &block)
    while i = self.index(key)
      self.delete_at(i)
      ret = key
    end
    return block.call if ret.nil? && block
    ret
  end

  ##
  # call-seq:
  #   array.sort! -> self
  #   array.sort! {|a, b| ... } -> self
  #
  # Sort all elements and replace +self+ with these
  # elements.
  def sort!(&block)
    stack = [ [ 0, self.size - 1 ] ]
    until stack.empty?
      left, mid, right = stack.pop
      if right == nil
        right = mid
        # sort self[left..right]
        if left < right
          if left + 1 == right
            lval = self[left]
            rval = self[right]
            cmp = if block then block.call(lval,rval) else lval <=> rval end
            if cmp.nil?
              raise ArgumentError, "comparison of #{lval.inspect} and #{rval.inspect} failed"
            end
            if cmp > 0
              self[left]  = rval
              self[right] = lval
            end
          else
            mid = ((left + right + 1) / 2).floor
            stack.push [ left, mid, right ]
            stack.push [ mid, right ]
            stack.push [ left, (mid - 1) ] if left < mid - 1
          end
        end
      else
        lary = self[left, mid - left]
        lsize = lary.size

        # The entity sharing between lary and self may cause a large memory
        # copy operation in the merge loop below. This harmless operation
        # cancels the sharing and provides a huge performance gain.
        lary[0] = lary[0]

        # merge
        lidx = 0
        ridx = mid
        (left..right).each { |i|
          if lidx >= lsize
            break
          elsif ridx > right
            self[i, lsize - lidx] = lary[lidx, lsize - lidx]
            break
          else
            lval = lary[lidx]
            rval = self[ridx]
            cmp = if block then block.call(lval,rval) else lval <=> rval end
            if cmp.nil?
              raise ArgumentError, "comparison of #{lval.inspect} and #{rval.inspect} failed"
            end
            if cmp <= 0
              self[i] = lval
              lidx += 1
            else
              self[i] = rval
              ridx += 1
            end
          end
        }
      end
    end
    self
  end

  ##
  # call-seq:
  #   array.sort -> new_array
  #   array.sort {|a, b| ... } -> new_array
  #
  # Returns a new Array whose elements are those from +self+, sorted.
  def sort(&block)
    self.dup.sort!(&block)
  end

  ##
  # call-seq:
  #   array.to_a -> self
  #
  # Returns self, no need to convert.
  def to_a
    self
  end
  alias entries to_a

  ##
  # Array is enumerable
  # ISO 15.2.12.3
  include Enumerable
end

# Array extensions (from mruby-enum-ext)
class Array
  ##
  # call-seq:
  #   array.sort_by {|element| ... } -> new_array
  #   array.sort_by                  -> enumerator
  #
  # Returns a new array with elements sorted by the values returned
  # from the block. Faster than +sort+ for expensive comparison
  # functions because the block is called only once per element.
  def sort_by(&block)
    return to_enum :sort_by unless block

    ary = []
    self.each_with_index{|e, i|
      ary.push([block.call(e), i])
    }
    if ary.size > 1
      ary.sort!
    end
    ary.collect!{|e,i| self[i]}
  end

  ##
  # call-seq:
  #   array.sort_by! {|element| ... } -> self
  #
  # Sorts +self+ in place by the values returned from the block.
  def sort_by!(&block)
    self.replace(self.sort_by(&block))
  end
end

##
# Hash
#
# Unordered mapping from keys to values (insertion order is preserved on
# iteration, matching CRuby). Keys must implement +hash+ and +eql?+;
# lookup is via +h[key]+. Includes Enumerable, supports a default value
# or default-proc fallback when a key is missing.
#
# ISO 15.2.13
class Hash
  ##
  # Hash is enumerable
  #
  # ISO 15.2.13.3
  include Enumerable

  ##
  # call-seq:
  #   hash == object -> true or false
  #
  #  Equality---Two hashes are equal if they each contain the same number
  #  of keys and if each key-value pair is equal to (according to
  #  <code>Object#==</code>) the corresponding elements in the other
  #  hash.
  #
  # ISO 15.2.13.4.1
  def ==(hash)
    return true if self.equal?(hash)
    unless Hash === hash
      return false
    end
    return false if self.size != hash.size
    self.each do |k,v|
      return false unless hash.key?(k)
      return false unless v == hash[k]
    end
    return true
  end

  ##
  # call-seq:
  #   hash.eql? object -> true or false
  #
  # Returns <code>true</code> if <i>hash</i> and <i>other</i> are
  # both hashes with the same content compared by eql?.
  #
  def eql?(hash)
    return true if self.equal?(hash)
    unless Hash === hash
      return false
    end
    return false if self.size != hash.size
    self.each do |k,v|
      return false unless hash.key?(k)
      return false unless self[k].eql?(hash[k])
    end
    return true
  end

  ##
  # call-seq:
  #   hash.delete(key) -> value or nil
  #   hash.delete(key) {|key| ... } -> object
  #
  # Delete the element with the key +key+.
  # Return the value of the element if +key+
  # was found. Return nil if nothing was
  # found. If a block is given, call the
  # block with the value of the element.
  #
  # ISO 15.2.13.4.8
  def delete(key, &block)
    if block && !self.has_key?(key)
      return block.call(key)
    end
    self.__delete(key)
  end

  ##
  # call-seq:
  #   hsh.each      {| key, value | block } -> hsh
  #   hsh.each_pair {| key, value | block } -> hsh
  #   hsh.each                              -> an_enumerator
  #   hsh.each_pair                         -> an_enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the key and value of each element.
  #
  # If no block is given, an enumerator is returned instead.
  #
  #     h = { "a" => 100, "b" => 200 }
  #     h.each {|key, value| puts "#{key} is #{value}" }
  #
  # <em>produces:</em>
  #
  # a is 100
  # b is 200
  #
  # ISO 15.2.13.4.9
  def each(&block)
    return to_enum :each unless block

    keys = self.keys
    vals = self.values
    len = self.size
    i = 0
    while i < len
      block.call [keys[i], vals[i]]
      i += 1
    end
    self
  end

  ##
  # call-seq:
  #   hsh.each_key {| key | block } -> hsh
  #   hsh.each_key                  -> an_enumerator
  #
  # Calls the given block for each element of +self+
  # and pass the key of each element.
  #
  # If no block is given, an enumerator is returned instead.
  #
  #   h = { "a" => 100, "b" => 200 }
  #   h.each_key {|key| puts key }
  #
  # <em>produces:</em>
  #
  #  a
  #  b
  #
  # ISO 15.2.13.4.10
  def each_key(&block)
    return to_enum :each_key unless block

    self.keys.each{|k| block.call(k)}
    self
  end

  ##
  # call-seq:
  #   hsh.each_value {| value | block } -> self
  #   hsh.each_value                    -> an_enumerator
  #
  # Calls the given block with each value; returns +self+:
  #
  # If no block is given, an enumerator is returned instead.
  #
  #  h = { "a" => 100, "b" => 200 }
  #  h.each_value {|value| puts value }
  #
  # <em>produces:</em>
  #
  #  100
  #  200
  #
  # ISO 15.2.13.4.11
  def each_value(&block)
    return to_enum :each_value unless block

    self.values.each{|v| block.call(v)}
    self
  end

  ##
  # call-seq:
  #     hsh.merge(other_hash..)                                 -> hsh
  #     hsh.merge(other_hash..){|key, oldval, newval| block}    -> hsh
  #
  #  Returns the new \Hash formed by merging each of +other_hashes+
  #  into a copy of +self+.
  #
  #  Each argument in +other_hashes+ must be a \Hash.
  #  Adds the contents of _other_hash_ to _hsh_. If no block is specified,
  #  entries with duplicate keys are overwritten with the values from
  #  _other_hash_, otherwise the value of each duplicate key is determined by
  #  calling the block with the key, its value in _hsh_ and its value in
  #  _other_hash_.
  #
  #  Example:
  #   h = {foo: 0, bar: 1, baz: 2}
  #   h1 = {bat: 3, bar: 4}
  #   h2 = {bam: 5, bat:6}
  #   h3 = h.merge(h1, h2) { |key, old_value, new_value| old_value + new_value }
  #   h3 # => {:foo=>0, :bar=>5, :baz=>2, :bat=>9, :bam=>5}
  #
  # ISO 15.2.13.4.22
  def merge(*others, &block)
    h = self.dup
    return h.__merge(*others) unless block
    i=0; len=others.size
    while i<len
      other = others[i]
      i += 1
      raise TypeError, "Hash required (#{other.class} given)" unless Hash === other
      other.each_key{|k|
        h[k] = (self.has_key?(k))? block.call(k, self[k], other[k]): other[k]
      }
    end
    h
  end

  ##
  #  call-seq:
  #     hsh.reject! {| key, value | block }  -> hsh or nil
  #     hsh.reject!                          -> an_enumerator
  #
  #  Equivalent to <code>Hash#delete_if</code>, but returns
  #  <code>nil</code> if no changes were made.
  #
  #  1.8/1.9 Hash#reject! returns Hash; ISO says nothing.
  #
  def reject!(&block)
    return to_enum :reject! unless block

    keys = []
    self.each{|k,v|
      if block.call([k, v])
        keys.push(k)
      end
    }
    return nil if keys.size == 0
    keys.each{|k|
      self.delete(k)
    }
    self
  end

  ##
  #  call-seq:
  #     hsh.reject {|key, value| block}   -> a_hash
  #     hsh.reject                        -> an_enumerator
  #
  #  Returns a new hash consisting of entries for which the block returns false.
  #
  #  If no block is given, an enumerator is returned instead.
  #
  #     h = { "a" => 100, "b" => 200, "c" => 300 }
  #     h.reject {|k,v| k < "b"}  #=> {"b" => 200, "c" => 300}
  #     h.reject {|k,v| v > 100}  #=> {"a" => 100}
  #
  #  1.8/1.9 Hash#reject returns Hash; ISO says nothing.
  #
  def reject(&block)
    return to_enum :reject unless block

    h = {}
    self.each{|k,v|
      unless block.call([k, v])
        h[k] = v
      end
    }
    h
  end

  ##
  #  call-seq:
  #     hsh.select! {| key, value | block }  -> hsh or nil
  #     hsh.select!                          -> an_enumerator
  #
  #  Equivalent to <code>Hash#keep_if</code>, but returns
  #  <code>nil</code> if no changes were made.
  #
  #  1.9 Hash#select! returns Hash; ISO says nothing.
  #
  def select!(&block)
    return to_enum :select! unless block

    keys = []
    self.each{|k,v|
      unless block.call([k, v])
        keys.push(k)
      end
    }
    return nil if keys.size == 0
    keys.each{|k|
      self.delete(k)
    }
    self
  end

  ##
  #  call-seq:
  #     hsh.select {|key, value| block}   -> a_hash
  #     hsh.select                        -> an_enumerator
  #
  #  Returns a new hash consisting of entries for which the block returns true.
  #
  #  If no block is given, an enumerator is returned instead.
  #
  #     h = { "a" => 100, "b" => 200, "c" => 300 }
  #     h.select {|k,v| k > "a"}  #=> {"b" => 200, "c" => 300}
  #     h.select {|k,v| v < 200}  #=> {"a" => 100}
  #
  #  1.9 Hash#select returns Hash; ISO says nothing
  #
  def select(&block)
    return to_enum :select unless block

    h = {}
    self.each{|k,v|
      if block.call([k, v])
        h[k] = v
      end
    }
    h
  end
end

##
# Integer
#
# ISO 15.2.8
##
class Integer
  ##
  # Calls the given block once for each Integer
  # from +self+ downto +num+.
  #
  # ISO 15.2.8.3.15
  def downto(num, &block)
    return to_enum(:downto, num) unless block

    i = self.to_i
    while i >= num
      block.call(i)
      i -= 1
    end
    self
  end


  ##
  # Calls the given block +self+ times.
  #
  # ISO 15.2.8.3.22
  def times(&block)
    return to_enum :times unless block

    i = 0
    while i < self
      block.call i
      i += 1
    end
    self
  end


  ##
  # Calls the given block once for each Integer
  # from +self+ upto +num+.
  #
  # ISO 15.2.8.3.27
  def upto(num, &block)
    return to_enum(:upto, num) unless block

    i = self.to_i
    while i <= num
      block.call(i)
      i += 1
    end
    self
  end
end

##
# Range
#
# An interval between two values, written as <code>begin..end</code>
# (inclusive) or <code>begin...end</code> (exclusive of end). Used for
# iteration, membership tests, and array slicing. Includes Enumerable
# when the endpoints support +succ+.
#
# ISO 15.2.14
class Range
  ##
  # Range is enumerable
  #
  # ISO 15.2.14.3
  include Enumerable

  ##
  # Calls the given block for each element of +self+
  # and pass the respective element.
  #
  # ISO 15.2.14.4.4
  def each(&block)
    return to_enum :each unless block

    val = self.begin
    last = self.end

    if val.kind_of?(Integer) && last.nil?
      i = val
      while true
        block.call(i)
        i += 1
      end
      return self
    end

    if val.kind_of?(String) && last.nil?
      if val.respond_to? :__upto_endless
        return val.__upto_endless(&block)
      else
        str_each = true
      end
    end

    if val.kind_of?(Integer) && last.kind_of?(Integer) # integers are special
      lim = last
      lim += 1 unless exclude_end?
      i = val
      while i < lim
        block.call(i)
        i += 1
      end
      return self
    end

    if val.kind_of?(String) && last.kind_of?(String) # strings are special
      if val.respond_to? :upto
        return val.upto(last, exclude_end?, &block)
      else
        str_each = true
      end
    end

    raise TypeError, "can't iterate" unless val.respond_to? :succ

    return self if (val <=> last) > 0

    while (val <=> last) < 0
      block.call(val)
      val = val.succ
      if str_each
        break if val.size > last.size
      end
    end

    block.call(val) if !exclude_end? && (val <=> last) == 0
    self
  end

  # redefine #hash 15.3.1.3.15
  def hash
    h = first.hash ^ last.hash
    h += 1 if self.exclude_end?
    h
  end

  ##
  # call-seq:
  #    rng.to_a                   -> array
  #    rng.entries                -> array
  #
  # Returns an array containing the items in the range.
  #
  #   (1..7).to_a  #=> [1, 2, 3, 4, 5, 6, 7]
  #   (1..).to_a   #=> RangeError: cannot convert endless range to an array
  def to_a
    a = __num_to_a
    return a if a
    super
  end
  alias entries to_a
end

##
# Interned, immutable identifier -- written +:name+. Two symbols with the
# same name are the same object, which makes them efficient as hash keys,
# method names, and tag values. Convert to and from strings with +to_s+
# and +String#to_sym+.
class Symbol
  ##
  # call-seq:
  #   sym.to_proc -> proc
  #
  # Returns a Proc that, when called with an object, invokes the method
  # named by +sym+ on that object with any additional arguments.
  #
  #   [1, 2, 3].map(&:to_s)   #=> ["1", "2", "3"]
  def to_proc
    mid = self
    ->(obj,*args,**opts,&block) do
      obj.__send__(mid, *args, **opts, &block)
    end
  end
end

##
# Comparable
#
# Mixin providing ordering and equality predicates (+<+, +<=+, +==+,
# +>+, +>=+, +between?+, +clamp+) on top of a host-defined +<=>+
# operator. Including classes need only implement +<=>+ to get the
# full comparison API for free.
#
# ISO 15.3.3
module Comparable

  ##
  # call-seq:
  #   obj < other    -> true or false
  #
  # Return true if +self+ is less
  # than +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.1
  def < other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp < 0
  end

  ##
  # call-seq:
  #   obj <= other   -> true or false
  #
  # Return true if +self+ is less
  # than or equal to +other+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.2
  def <= other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp <= 0
  end

  ##
  # call-seq:
  #   obj == other   -> true or false
  #
  # Return true if +self+ is equal
  # to +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.3
  def == other
    cmp = self <=> other
    cmp.equal?(0)
  end

  ##
  # call-seq:
  #   obj > other    -> true or false
  #
  # Return true if +self+ is greater
  # than +other+. Otherwise return
  # false.
  #
  # ISO 15.3.3.2.4
  def > other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp > 0
  end

  ##
  # call-seq:
  #   obj >= other   -> true or false
  #
  # Return true if +self+ is greater
  # than or equal to +other+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.5
  def >= other
    cmp = self <=> other
    if cmp.nil?
      raise ArgumentError, "comparison of #{self.class} with #{other.class} failed"
    end
    cmp >= 0
  end

  ##
  # call-seq:
  #   obj.between?(min,max) -> true or false
  #
  # Return true if +self+ is greater
  # than or equal to +min+ and
  # less than or equal to +max+.
  # Otherwise return false.
  #
  # ISO 15.3.3.2.6
  def between?(min, max)
    self >= min and self <= max
  end
end

##
# String
#
# Mutable sequence of bytes, usually interpreted as UTF-8 text. Indexing
# returns substrings or single characters; many methods come in pairs of
# pure (+upcase+) and in-place (+upcase!+) variants. Includes Comparable.
#
# ISO 15.2.10
class String
  # ISO 15.2.10.3
  include Comparable

  ##
  # Calls the given block for each line
  # and pass the respective line.
  #
  # ISO 15.2.10.5.15
  def each_line(separator = "\n", &block)
    return to_enum(:each_line, separator) unless block

    if separator.nil?
      block.call(self)
      return self
    end
    raise TypeError unless separator.is_a?(String)

    paragraph_mode = false
    if separator.empty?
      paragraph_mode = true
      separator = "\n\n"
    end
    start = 0
    string = dup
    self_len = self.bytesize
    sep_len = separator.bytesize

    while (pointer = string.byteindex(separator, start))
      pointer += sep_len
      pointer += 1 while paragraph_mode && string.getbyte(pointer) == 10 # 10 == \n
      block.call(string.byteslice(start, pointer - start))
      start = pointer
    end
    return self if start == self_len

    block.call(string.byteslice(start, self_len - start))
    self
  end

  ##
  # Replace all matches of +pattern+ with +replacement+.
  # Call block (if given) for each match and replace
  # +pattern+ with the value of the block. Return the
  # final value.
  #
  # ISO 15.2.10.5.18
  def gsub(*args, &block)
    return to_enum(:gsub, *args) if args.length == 1 && !block
    raise ArgumentError, "wrong number of arguments (given #{args.length}, expected 1..2)" unless (1..2).include?(args.length)

    pattern, replace = *args
    plen = pattern.length
    if args.length == 2 && block
      block = nil
    end
    offset = 0
    result = []
    while found = self.byteindex(pattern, offset)
      result << self.byteslice(offset, found - offset)
      offset = found + plen
      result << if block
        block.call(pattern).to_s
      else
        self.__sub_replace(replace, pattern, found)
      end
      if plen == 0
        result << self.byteslice(offset, 1)
        offset += 1
      end
    end
    result << self.byteslice(offset..-1) if offset < length
    result.join
  end

  ##
  # Replace all matches of +pattern+ with +replacement+.
  # Call block (if given) for each match and replace
  # +pattern+ with the value of the block. Modify
  # +self+ with the final value.
  #
  # ISO 15.2.10.5.19
  def gsub!(*args, &block)
    raise FrozenError, "can't modify frozen String" if frozen?
    return to_enum(:gsub!, *args) if args.length == 1 && !block
    str = self.gsub(*args, &block)
    return nil unless self.index(args[0])
    self.replace(str)
  end

#  ##
#  # Calls the given block for each match of +pattern+
#  # If no block is given return an array with all
#  # matches of +pattern+.
#  #
#  # ISO 15.2.10.5.32
#  def scan(pattern, &block)
#    # TODO: String#scan is not implemented yet
#  end

  ##
  # Replace only the first match of +pattern+ with
  # +replacement+. Call block (if given) for each
  # match and replace +pattern+ with the value of the
  # block. Return the final value.
  #
  # ISO 15.2.10.5.36
  def sub(*args, &block)
    unless (1..2).include?(args.length)
      raise ArgumentError, "wrong number of arguments (given #{args.length}, expected 2)"
    end

    pattern, replace = *args
    if args.length == 2 && block
      block = nil
    end
    result = []
    found = self.index(pattern)
    return self.dup unless found
    result << self.byteslice(0, found)
    offset = found + pattern.length
    result << if block
      block.call(pattern).to_s
    else
      self.__sub_replace(replace, pattern, found)
    end
    result << self.byteslice(offset..-1) if offset < length
    result.join
  end

  ##
  # Replace only the first match of +pattern+ with
  # +replacement+. Call block (if given) for each
  # match and replace +pattern+ with the value of the
  # block. Modify +self+ with the final value.
  #
  # ISO 15.2.10.5.37
  def sub!(*args, &block)
    raise FrozenError, "can't modify frozen String" if frozen?
    str = self.sub(*args, &block)
    return nil unless self.index(args[0])
    self.replace(str)
  end

  ##
  # Call the given block for each byte of +self+.
  def each_byte(&block)
    return to_enum(:each_byte, &block) unless block
    pos = 0
    while pos < bytesize
      block.call(getbyte(pos))
      pos += 1
    end
    self
  end

  # those two methods requires Regexp that is optional in mruby
  ##
  # ISO 15.2.10.5.3
  #def =~(re)
  # re =~ self
  #end

  ##
  # ISO 15.2.10.5.27
  #def match(re, &block)
  #  re.match(self, &block)
  #end
end

##
# Raised by Enumerator#next when iteration has finished. Internal
# iterators like +loop+ catch this exception to terminate cleanly.
# The +#result+ accessor exposes the value the iterator returned.
#
# StopIteration#result accessor (from mruby-fiber upstream)
class StopIteration
  ##
  # The value returned by the enumeration's terminating method (e.g. the
  # block return value of an Enumerator).
  attr_accessor :result
end

##
# Enumerator
#
# A class which allows both internal and external iteration.
# (from mruby-enumerator)
class Enumerator
  include Enumerable

  ##
  # call-seq:
  #   Enumerator.new(obj, method=:each, *args, **kwd) -> enumerator
  #   Enumerator.new {|yielder| ... }                  -> enumerator
  #
  # Creates a new Enumerator. With an object, iteration is delegated to
  # +obj.method(*args, **kwd)+. With a block, the block receives a
  # Yielder and produces values on demand.
  def initialize(obj=Enumerable::NONE, meth=:each, *args, **kwd, &block)
    if block
      obj = Generator.new(&block)
    elsif Enumerable::NONE.equal?(obj)
      raise ArgumentError, "wrong number of arguments (given 0, expected 1+)"
    end

    @obj = obj
    @meth = meth
    @args = args
    @kwd = kwd
    @fib = nil
    @dst = nil
    @lookahead = nil
    @feedvalue = nil
    @stop_exc = false
  end

  ##
  # call-seq:
  #   enum.initialize_copy(other) -> self
  #
  # Copies internal state from another Enumerator. Raises TypeError if
  # +other+ is not an Enumerator or has an active execution context.
  def initialize_copy(obj)
    raise TypeError, "can't copy type #{obj.class}" unless obj.kind_of? Enumerator
    raise TypeError, "can't copy execution context" if obj.instance_eval{@fib}
    meth = args = kwd = fib = nil
    obj.instance_eval {
      obj = @obj
      meth = @meth
      args = @args
      kwd = @kwd
    }
    @obj = obj
    @meth = meth
    @args = args
    @kwd = kwd
    @fib = nil
    @lookahead = nil
    @feedvalue = nil
    self
  end

  ##
  # call-seq:
  #   enum.with_index(offset=0) {|element, index| ... } -> object
  #   enum.with_index(offset=0)                         -> enumerator
  #
  # Iterates the block with each element and an index starting from
  # +offset+. Returns the value returned by the underlying iteration.
  def with_index(offset=0, &block)
    return to_enum :with_index, offset unless block

    if offset.nil?
      offset = 0
    else
      offset = offset.__to_int
    end

    n = offset - 1
    __enumerator_block_call do |*i|
      n += 1
      block.call i.__svalue, n
    end
  end

  ##
  # call-seq:
  #   enum.each_with_index {|element, index| ... } -> object
  #   enum.each_with_index                         -> enumerator
  #
  # Iterates the block with each element and its zero-based index.
  # Shorthand for <code>with_index(0)</code>.
  def each_with_index(&block)
    with_index(0, &block)
  end

  ##
  # call-seq:
  #   enum.with_object(obj) {|element, obj| ... } -> obj
  #   enum.with_object(obj)                       -> enumerator
  #
  # Iterates the block with each element and the given +obj+, returning
  # +obj+ after iteration completes.
  def with_object(object, &block)
    return to_enum(:with_object, object) unless block

    __enumerator_block_call do |i|
      block.call [i,object]
    end
    object
  end

  ##
  # call-seq:
  #   enum.inspect -> string
  #
  # Returns a string representation including the receiver, the method
  # name, and any arguments.
  def inspect
    if @args && @args.size > 0
      args = @args.join(", ")
      "#<#{self.class}: #{@obj.inspect}:#{@meth}(#{args})>"
    else
      "#<#{self.class}: #{@obj.inspect}:#{@meth}>"
    end
  end

  ##
  # call-seq:
  #   enum.size -> integer or nil
  #
  # Returns the size of the enumerator, if known. Otherwise returns
  # nil. Delegates to <code>@obj.size</code> when available.
  def size
    if @size
      @size
    elsif @obj.respond_to?(:size)
      @obj.size
    end
  end

  ##
  # call-seq:
  #   enum.each(*args) {|element| ... } -> object
  #   enum.each(*args)                  -> enumerator
  #
  # Iterates the underlying object's enumerated method, optionally
  # appending +args+ to the originally bound arguments.
  def each(*argv, &block)
    obj = self
    if 0 < argv.length
      obj = self.dup
      args = obj.instance_eval{@args}
      if !args.empty?
        args = args.dup
        args.concat argv
      else
        args = argv.dup
      end
      obj.instance_eval{@args = args}
    end
    return obj unless block
    __enumerator_block_call(&block)
  end

  ##
  # Internal helper used by Enumerator iteration methods to invoke the
  # bound method on the wrapped object with stored arguments.
  def __enumerator_block_call(&block)
    @obj.__send__ @meth, *@args, **@kwd, &block
  end
  private :__enumerator_block_call

  ##
  # call-seq:
  #   enum.next -> object
  #
  # Returns the next element in external iteration. Raises
  # StopIteration when iteration is complete.
  def next
    next_values.__svalue
  end

  ##
  # call-seq:
  #   enum.next_values -> array
  #
  # Returns the next element as an array of values yielded by the
  # underlying iteration. Raises StopIteration at the end.
  def next_values
    if @lookahead
      vs = @lookahead
      @lookahead = nil
      return vs
    end
    raise @stop_exc if @stop_exc

    curr = Fiber.current

    if !@fib || !@fib.alive?
      @dst = curr
      @fib = Fiber.new do
        result = each do |*args|
          feedvalue = nil
          Fiber.yield args
          if @feedvalue
            feedvalue = @feedvalue
            @feedvalue = nil
          end
          feedvalue
        end
        @stop_exc = StopIteration.new "iteration reached an end"
        @stop_exc.result = result
        Fiber.yield nil
      end
      @lookahead = nil
    end

    vs = @fib.resume curr
    if @stop_exc
      @fib = nil
      @dst = nil
      @lookahead = nil
      @feedvalue = nil
      raise @stop_exc
    end
    vs
  end

  ##
  # call-seq:
  #   enum.peek -> object
  #
  # Returns the next element without advancing iteration. Raises
  # StopIteration at the end.
  def peek
    peek_values.__svalue
  end

  ##
  # call-seq:
  #   enum.peek_values -> array
  #
  # Returns the next element as an array, without advancing iteration.
  def peek_values
    if @lookahead.nil?
      @lookahead = next_values
    end
    @lookahead.dup
  end

  ##
  # call-seq:
  #   enum.rewind -> self
  #
  # Resets external iteration to the beginning. Calls +rewind+ on the
  # underlying object if it responds to it.
  def rewind
    @obj.rewind if @obj.respond_to? :rewind
    @fib = nil
    @dst = nil
    @lookahead = nil
    @feedvalue = nil
    @stop_exc = false
    self
  end

  ##
  # call-seq:
  #   enum.feed(value) -> nil
  #
  # Sets the value that will be returned from the next +yield+ in the
  # underlying iteration. Raises TypeError if a feed value is already
  # set.
  def feed(value)
    raise TypeError, "feed value already set" if @feedvalue
    @feedvalue = value
    nil
  end

  ##
  # Internal helper used by Enumerator.new(&block). Wraps a block that
  # receives a Yielder and produces values for the enumerator. Includes
  # Enumerable, so it can drive +each+-based iteration directly.
  class Generator
    include Enumerable
    ##
    # Creates a Generator wrapping the given block. The block is
    # invoked with a Yielder when iteration begins.
    def initialize(&block)
      raise TypeError, "wrong argument type #{self.class} (expected Proc)" unless block.kind_of? Proc

      @proc = block
    end

    ##
    # Runs the generator block with a fresh Yielder bound to the given
    # block, producing values for the consumer.
    def each(*args, &block)
      args.unshift Yielder.new(&block)
      @proc.call(*args)
    end
  end

  ##
  # Internal helper passed to the block of Enumerator.new. Each call to
  # +yield+ (or +<<+) on a Yielder invokes the consumer block with the
  # given value, producing one element of the enumeration.
  class Yielder
    ##
    # Creates a Yielder wrapping a callback block. The block is invoked
    # for every yielded value.
    def initialize(&block)
      raise LocalJumpError, "no block given" unless block

      @proc = block
    end

    ##
    # call-seq:
    #   yielder.yield(*args)
    #
    # Yields +args+ to the consumer of this Yielder.
    def yield(*args)
      @proc.call(*args)
    end

    ##
    # call-seq:
    #   yielder << value -> self
    #
    # Alias-like operator form for #yield; returns +self+ to allow
    # chaining.
    def << *args
      self.yield(*args)
      self
    end
  end

  ##
  # call-seq:
  #   Enumerator.produce(initial=nil) {|prev| ... } -> enumerator
  #
  # Creates an infinite enumerator that produces values by repeatedly
  # calling the block with the previously generated value (or +initial+
  # the first time). Iteration ends when the block raises
  # StopIteration.
  def Enumerator.produce(init=Enumerable::NONE, &block)
    raise ArgumentError, "no block given" if block.nil?
    Enumerator.new do |y|
      if Enumerable::NONE.equal?(init)
        val = nil
      else
        val = init
        y.yield(val)
      end
      begin
        while true
          y.yield(val = block.call(val))
        end
      rescue StopIteration
        # do nothing
      end
    end
  end
end

module Kernel
  ##
  # call-seq:
  #   obj.to_enum(method=:each, *args, **kwd)   -> enumerator
  #   obj.enum_for(method=:each, *args, **kwd)  -> enumerator
  #
  # Returns a new Enumerator that, when iterated, calls +method+ on
  # +obj+ with the given arguments.
  def to_enum(meth=:each, *args, **kwd)
    Enumerator.new self, meth, *args, **kwd
  end
  alias enum_for to_enum
end

module Enumerable
  # use Enumerator to use infinite sequence
  def zip(*args, &block)
    args = args.map do |a|
      if a.respond_to?(:each)
        a.to_enum(:each)
      else
        raise TypeError, "wrong argument type #{a.class} (must respond to :each)"
      end
    end

    result = block ? nil : []

    each do |*val|
      tmp = [val.__svalue]
      args.each do |arg|
        v = if arg.nil?
          nil
        else
          begin
            arg.next
          rescue StopIteration
            nil
          end
        end
        tmp.push(v)
      end
      if result.nil?
        block.call(tmp)
      else
        result.push(tmp)
      end
    end

    result
  end

  ##
  # call-seq:
  #   enum.chunk {|element| ... } -> enumerator
  #   enum.chunk                  -> enumerator
  #
  # Enumerates consecutive elements grouped by the block's return
  # value, yielding <code>[value, [elements...]]</code> pairs. Returns
  # +:_alone+ to put an element in its own chunk, or +:_separator+ /
  # +nil+ to drop it.
  def chunk(&block)
    return to_enum :chunk unless block

    enum = self
    Enumerator.new do |y|
      last_value, arr = nil, []
      enum.each do |element|
        value = block.call(element)
        case value
        when :_alone
          y.yield [last_value, arr] if arr.size > 0
          y.yield [value, [element]]
          last_value, arr = nil, []
        when :_separator, nil
          y.yield [last_value, arr] if arr.size > 0
          last_value, arr = nil, []
        when last_value
          arr << element
        else
          raise 'symbols beginning with an underscore are reserved' if value.is_a?(Symbol) && value.to_s[0] == '_'
          y.yield [last_value, arr] if arr.size > 0
          last_value, arr = value, [element]
        end
      end
      y.yield [last_value, arr] if arr.size > 0
    end
  end

  ##
  # call-seq:
  #   enum.chunk_while {|i, j| ... } -> enumerator
  #
  # Splits +enum+ into chunks where the block returns false for adjacent
  # element pairs. Returns an enumerator that yields each chunk as an
  # array.
  def chunk_while(&block)
    enum = self
    Enumerator.new do |y|
      n = 0
      last_value, arr = nil, []
      enum.each do |element|
        if n > 0
          unless block.call(last_value, element)
            y.yield arr
            arr = []
          end
        end
        arr.push(element)
        n += 1
        last_value = element
      end
      y.yield arr if arr.size > 0
    end
  end
end
