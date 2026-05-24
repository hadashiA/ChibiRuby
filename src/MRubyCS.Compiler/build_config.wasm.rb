# Requires the Emscripten SDK (emcc, em++, emar) on PATH.
# Produces a static archive (libmruby.a) for Unity WebGL and .NET Browser WASM.
# Both targets statically link the archive into the final wasm module, so we
# do not emit a shared library here (mrbgem.rake skips its shared-lib step
# because 'browser-wasm' does not match its windows|macOS|android pattern).

MRuby::CrossBuild.new('browser-wasm') do |conf|
  conf.toolchain :emscripten

  conf.gem './mruby-compiler2'
  conf.gem './mrbgems/mrubycs-compiler'

  conf.compilers.each do |cc|
    cc.defines = %w(MRB_WORD_BOXING MRC_TARGET_MRUBY MRC_ALLOC_LIBC)
  end
end
