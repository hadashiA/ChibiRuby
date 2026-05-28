MRuby::CrossBuild.new("macos-arm64") do |conf|
  conf.toolchain :clang

  #conf.gem core: 'mruby-compiler'
  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/chibiruby-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch arm64'
  conf.linker.flags << '-arch arm64'
end

MRuby::CrossBuild.new("macos-x64") do |conf|
  conf.toolchain :clang

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/chibiruby-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  conf.cc.flags << '-arch x86_64'
  conf.linker.flags << '-arch x86_64'
end
