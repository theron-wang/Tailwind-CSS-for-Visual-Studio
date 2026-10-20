using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration.Descriptions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Configuration;

/// <summary>
/// Check ConfigurationClassGenerator.cs for the other half
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed partial class CompletionConfiguration
{
    internal Func<Task>? ConfigurationUpdated;

    [Import]
    internal ConfigurationFileReloader Reloader { get; set; } = null!;

    [Import(typeof(GeneratorAggregator))]
    internal GeneratorAggregator DescriptionGenerator { get; set; } = null!;

    [Import]
    internal ColorIconGenerator ColorIconGenerator { get; set; } = null!;

    [Import]
    internal DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;

    [Import]
    public ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    [Import]
    public ProjectConfigurationInitializer ProjectConfigurationInitializer { get; set; } = null!;

    /// <summary>
    /// Shorthand for calling <see cref="ReloadCustomAttributesAsync(ConfigurationFile, TailwindSettings)"/> on all projects
    /// </summary>
    public async Task ReloadCustomAttributesAsync(TailwindSettings settings)
    {
        var failed = false;

        foreach (var configurationFile in settings.ConfigurationFiles.ToList())
        {
            var success = await ReloadCustomAttributesImplAsync(configurationFile, settings);

            if (!success)
            {
                failed = true;
            }
        }

        await NotifyConfigurationUpdatedAsync();

        if (!failed && settings.ConfigurationFiles.Count > 0)
        {
            await VS.StatusBar.ShowMessageAsync("Successfully reloaded Tailwind CSS configuration");
        }
    }

    /// <summary>
    /// Adjusts classes to match a change in the configuration file
    /// </summary>
    public async Task ReloadCustomAttributesAsync(
        ConfigurationFile configurationFile,
        TailwindSettings settings
    )
    {
        var success = await ReloadCustomAttributesImplAsync(configurationFile, settings);

        await NotifyConfigurationUpdatedAsync();

        if (success)
        {
            await VS.StatusBar.ShowMessageAsync("Successfully reloaded Tailwind CSS configuration");
        }
    }

    /// <summary>
    /// Implementation for <see cref="ReloadCustomAttributesAsync(ConfigurationFile, TailwindSettings)"/>. Runs on a background thread, as this
    /// can be expensive.
    /// </summary>
    /// <returns>
    /// True if run successfully, false if an error occurred
    /// </returns>
    private async Task<bool> ReloadCustomAttributesImplAsync(
        ConfigurationFile configurationFile,
        TailwindSettings settings
    )
    {
        var request = await ProjectConfigurationManager.BeginReloadAsync(configurationFile.Path);
        if (request is null)
        {
            return false;
        }

        var version = await DirectoryVersionFinder.GetTailwindVersionAsync(
            configurationFile.Path,
            settings
        );
        var replacement = await CreateCompletionSnapshotAsync(configurationFile, version);
        return replacement is not null
            && await ProjectConfigurationManager.TryPublishReloadAsync(request, replacement);
    }

    internal async Task NotifyConfigurationUpdatedAsync()
    {
        var listeners = ConfigurationUpdated;
        if (listeners is not null)
        {
            await TaskScheduler.Default;
            await Task.WhenAll(listeners.GetInvocationList().Cast<Func<Task>>().Select(d => d()));
        }
    }

    internal async Task<ProjectCompletionValues?> CreateCompletionSnapshotAsync(
        ConfigurationFile file,
        TailwindVersion version
    )
    {
        await VS.StatusBar.ShowMessageAsync("Reloading Tailwind CSS configuration");
        try
        {
            await TaskScheduler.Default;
            var config = await ConfigFileParser.GetConfigurationAsync(file.Path, version);
            // Generators only mutate this private copy. Readers retain the old,
            // complete snapshot until the manager publishes the replacement.
            var project = (
                await ProjectConfigurationInitializer.GetUnsetCompletionConfigurationAsync(version)
            ).Copy();
            project.FilePath = file.Path;
            project.Version = version;
            project.LoadedConfiguration = config;
            project.ApplicablePaths = [.. config.ContentPaths.Where(c => !c.StartsWith("!"))];
            project.NotApplicablePaths =
            [
                .. config.ContentPaths.Where(c => c.StartsWith("!")).Select(c => c.TrimStart('!')),
            ];
            project.Prefix =
                version >= TailwindVersion.V4 && !string.IsNullOrWhiteSpace(config.Prefix)
                    ? $"{config.Prefix}:"
                    : config.Prefix;
            await LoadGlobalConfigurationAsync(project, config);
            await LoadIndividualConfigurationOverrideAsync(project, config);
            LoadIndividualConfigurationExtend(project, config);
            await LoadPluginsAsync(project, config);
            project.Variants = [.. project.Variants.Distinct()];
            await VS.StatusBar.ShowMessageAsync("Finished reloading Tailwind CSS configuration");
            return project;
        }
        catch (Exception ex)
        {
            await VS.StatusBar.ShowMessageAsync(
                "Tailwind CSS: Failed to load configuration file; check the 'Extensions' output window for more details"
            );
            await ex.LogAsync();
            return null;
        }
    }
}
