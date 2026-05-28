# For x64 linux machine
MRuby::CrossBuild.new("linux-x64") do |conf|
  conf.toolchain :gcc

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/chibiruby-compiler'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
    cc.flags << '-fPIC'
  end

  conf.archiver do |archiver|
    archiver.command = cc.command
    archiver.archive_options = '-shared -o %{outfile} %{objs}'
  end

  conf.linker do |linker|
    linker.flags = ['-Wl,--whole-archive']
    linker.libraries = %w(m)
  end

  # file extensions
  conf.exts do |exts|
    exts.library = '.so'
  end
end

MRuby::CrossBuild.new("linux-arm64") do |conf|
  conf.toolchain :gcc

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/chibiruby-compiler'
  
  conf.cc.command = 'aarch64-linux-gnu-gcc'
  conf.linker.command = 'aarch64-linux-gnu-gcc'
  conf.archiver.command = 'aarch64-linux-gnu-ar'

  # Note: mruby 4.0 removed MRB_NO_PRESYM and `disable_presym`; presym is always on.
  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
    cc.flags << '-fPIC'
  end

  conf.archiver do |archiver|
    archiver.command = cc.command
    archiver.archive_options = '-shared -o %{outfile} %{objs}'
  end

  conf.linker do |linker|
    linker.flags = ['-Wl,--whole-archive']    
    linker.libraries = %w(m)
  end

  # file extensions
  conf.exts do |exts|
    exts.library = '.so'
  end
end
