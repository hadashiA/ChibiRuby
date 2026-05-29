# Requires Xcode (xcrun) on macOS.
# Produces static libraries (libmruby.a) for iOS device and simulator.
# Apple's App Store policy disallows dynamic linking of arbitrary libraries,
# so the iOS variants must be static and linked into the host app.

def ios_sdk_path(sdk)
  `xcrun --sdk #{sdk} --show-sdk-path`.strip
end

def ios_clang
  `xcrun --sdk iphoneos --find clang`.strip
end

def ios_ar
  `xcrun --find ar`.strip
end

%w(ios-arm64 iossimulator-arm64).each do |target|
  MRuby::CrossBuild.new(target) do |conf|
    conf.toolchain :clang

    conf.gem './mruby-compiler2'
    conf.gem './mrbgems/chibiruby-compiler'

    sdk, arch, min_flag = case target
                          when 'ios-arm64'
                            ['iphoneos',      'arm64', '-miphoneos-version-min=13.0']
                          when 'iossimulator-arm64'
                            ['iphonesimulator', 'arm64', '-mios-simulator-version-min=13.0']
                          end

    sysroot = ios_sdk_path(sdk)

    conf.cc.command = ios_clang
    conf.cxx.command = ios_clang
    conf.asm.command = ios_clang
    conf.linker.command = ios_clang
    conf.archiver.command = ios_ar

    common_flags = %W(-arch #{arch} -isysroot #{sysroot} #{min_flag})

    conf.compilers.each do |cc|
      cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
      common_flags.each { |f| cc.flags << f }
    end
    common_flags.each { |f| conf.linker.flags << f }
  end
end
