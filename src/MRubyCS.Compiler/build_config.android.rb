# Requires Android NDK r23+ (clang toolchain).
# Set ANDROID_NDK_HOME (or :ndk_home param) so mruby's :android toolchain can find clang.

MRuby::CrossBuild.new('android-arm64') do |conf|
  toolchain :android, arch: 'arm64-v8a', sdk_version: 24, toolchain: :clang

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  end
end

MRuby::CrossBuild.new('android-x64') do |conf|
  toolchain :android, arch: 'x86_64', sdk_version: 24, toolchain: :clang

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  end
end
