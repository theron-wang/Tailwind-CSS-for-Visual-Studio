using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TailwindCSSIntellisense.Completions;

public sealed partial class ProjectConfigurationManager
{
    internal static ImageSource TailwindLogo { get; private set; } =
        new BitmapImage(
            new Uri(
                Path.Combine(
                    PathHelpers.GetContainingDirectory(Assembly.GetExecutingAssembly().Location),
                    "Resources",
                    "tailwindlogo.png"
                ),
                UriKind.Relative
            )
        );
}
