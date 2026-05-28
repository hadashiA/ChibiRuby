MRuby::Gem::Specification.new('mrubycs-compiler') do |spec|
  spec.license = 'MIT'
  spec.authors = 'hadashiA'
end

MRuby.each_target do
  next unless name.match(/^(windows|macOS|android)/i)

  # Pick the shared-library extension based on the *target* (not the host)
  # so cross-builds (e.g. Android-on-macOS) produce the correct file name.
  sharedlib_ext =
    if name.match(/^macOS/i)
      'dylib'
    elsif name.match(/^windows/i)
      'dll'
    else
      'so'
    end
  
  mruby_sharedlib = "#{build_dir}/lib/libmruby.#{sharedlib_ext}"

  products << mruby_sharedlib

  file shared_lib: mruby_sharedlib

  task mruby_sharedlib => libmruby_static do |t|
    is_vc = primary_toolchain == 'visualcpp'
    is_mingw = ENV['OS'] == 'Windows_NT' && cc.command.start_with?('gcc')

    deffile = "#{File.dirname(__FILE__)}/mrubycs-compiler.def"

    flags = []
    flags_after_libraries = []
    
    if is_vc
      flags << '/DLL'
      flags << "/DEF:#{deffile}"
      flags << libmruby_static
    else
      flags << '-shared'
      flags << '-fpic'
      if sharedlib_ext == 'dylib'
        #flags << '-Wl,-undefined,dynamic_lookup'
        flags << "-Wl,-force_load #{libmruby_static}"
        # flags << '-install_name @rpath/libmruby.dylib'
      elsif is_mingw
        flags << deffile
        flags << libmruby_static
      else
        #flags << '--allow-shlib-undefined'
        flags << "-Wl,--whole-archive #{libmruby_static}"
        flags_after_libraries << '-Wl,--no-whole-archive'
      end
    end

    flags << "/MACHINE:#{ENV['Platform']}" if is_vc && ENV.include?('Platform')
    flags += flags_after_libraries

    linker.run mruby_sharedlib, [], [], [], flags

    # tools = File.exapnd_path('../tools.rb', __FILE__)
    # sh "ruby #{tools} copy_to_uity #{build_dir}"
  end
end
