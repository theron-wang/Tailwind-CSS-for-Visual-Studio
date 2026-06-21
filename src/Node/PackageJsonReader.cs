using System.ComponentModel.Composition;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Node;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class PackageJsonReader
{
    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    internal async Task<(bool exists, string? fileName)> ScriptExistsAsync(
        string configFileName,
        string? scriptName
    )
    {
        if (scriptName is null)
        {
            return (exists: false, fileName: null);
        }

        var settings = await SettingsProvider.GetSettingsAsync();
        string packageJsonFileName;

        if (
            settings.PackageConfigurationFile is not null
            && File.Exists(settings.PackageConfigurationFile)
        )
        {
            packageJsonFileName = settings.PackageConfigurationFile;
        }
        else
        {
            if (configFileName is null)
            {
                return (exists: false, fileName: null);
            }

            packageJsonFileName = Path.Combine(
                Path.GetDirectoryName(configFileName),
                "package.json"
            );

            if (packageJsonFileName is null || !File.Exists(packageJsonFileName))
            {
                return (exists: false, fileName: null);
            }
        }

        try
        {
            JsonObject? file;

            using (
                var fs = File.Open(
                    packageJsonFileName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                )
            )
            {
                file = await JsonSerializer.DeserializeAsync<JsonObject>(fs);
            }

            return (
                exists: file?["scripts"]?[scriptName] is not null,
                fileName: packageJsonFileName
            );
        }
        catch
        {
            return (exists: false, fileName: packageJsonFileName);
        }
    }
}
