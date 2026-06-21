using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;

namespace TailwindCSSIntellisense.Completions.TCss;

internal static class TCssFileAndContentTypeDeclaration
{
#pragma warning disable CS0649
#pragma warning disable CS8618
    [Export]
    [Name("tcss")]
    [BaseDefinition("css")]
    internal static ContentTypeDefinition TCssContentTypeDefinition;

    [Export]
    [FileExtension(".tcss")]
    [ContentType("tcss")]
    internal static FileExtensionToContentTypeDefinition TCssFileExtensionDefinition;
#pragma warning restore
}
