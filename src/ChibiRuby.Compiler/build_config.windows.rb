MRuby::CrossBuild.new("windows") do |conf|
  conf.toolchain

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/chibiruby-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
end
