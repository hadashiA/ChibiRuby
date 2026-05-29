using Microsoft.CodeAnalysis;

namespace ChibiRuby.Serializer.SourceGenerator;

public class ReferenceSymbols
{
    public static ReferenceSymbols? Create(Compilation compilation)
    {
        var mrubyObjectAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyObjectAttribute");
        if (mrubyObjectAttribute is null)
            return null;

        return new ReferenceSymbols
        {
            MRubyObjectAttribute = mrubyObjectAttribute,
            MRubyMemberAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyMemberAttribute")!,
            MRubyIgnoreAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyIgnoreAttribute")!,
            MRubyConstructorAttribute = compilation.GetTypeByMetadataName("ChibiRuby.Serializer.MRubyConstructorAttribute")!,
        };
    }

    public INamedTypeSymbol MRubyObjectAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyMemberAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyIgnoreAttribute { get; private set; } = default!;
    public INamedTypeSymbol MRubyConstructorAttribute { get; private set; } = default!;
}
