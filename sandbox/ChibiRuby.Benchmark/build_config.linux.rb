MRuby::CrossBuild.new("linux-x64") do |conf|
  conf.toolchain :gcc

  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'
  conf.gem core: 'mruby-math'
  conf.gem './mrbgems/chibiruby-benchmark-helper'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
  conf.cc.flags << '-fPIC'
end

MRuby::CrossBuild.new("linux-arm64") do |conf|
  conf.toolchain :gcc

  conf.gem core: 'mruby-compiler'
  conf.gem core: 'mruby-string-ext'
  conf.gem core: 'mruby-math'  
  conf.gem './mrbgems/chibiruby-benchmark-helper'

  conf.cc.command = 'aarch64-linux-gnu-gcc'
  conf.linker.command = 'aarch64-linux-gnu-gcc'
  conf.archiver.command = 'aarch64-linux-gnu-ar'

  conf.disable_presym
  conf.cc.defines = %w(MRB_WORD_BOXING MRB_NO_PRESYM)
  conf.cc.flags << '-fPIC'
end
