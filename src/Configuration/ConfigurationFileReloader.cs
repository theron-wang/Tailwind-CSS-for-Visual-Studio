using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Configuration;

/// <summary>
/// Reloads Intellisense when the Tailwind CSS configuration file is modified
/// </summary>
[Export]
public sealed class ConfigurationFileReloader : IDisposable
{
    [Import]
    internal ConfigFileScanner Scanner { get; set; } = null!;

    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [Import]
    internal CompletionConfiguration CompletionConfiguration { get; set; } = null!;

    private bool _subscribed;

    private TailwindSettings _settings = null!;

    private readonly SemaphoreSlim _importToConfigSemaphore = new(1, 1);
    private readonly Dictionary<string, HashSet<ConfigurationFile>> _importToConfigurationFiles =
        new(StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// Initializes the class to subscribe to relevant events
    /// </summary>
    /// <param name="fromNewProject">Should only be called from <see cref="TailwindCSSIntellisensePackage"/> to reset the configuration file path</param>
    public async Task InitializeAsync()
    {
        if (_subscribed == false)
        {
            VS.Events.DocumentEvents.Saved += OnFileSave;
            SettingsProvider.OnSettingsChanged += OnSettingsChangedAsync;

            _subscribed = true;
        }

        _settings = await SettingsProvider.GetSettingsAsync();
        await CompletionConfiguration.ReloadCustomAttributesAsync(_settings);
    }

    public async Task AddImportAsync(string import, ConfigurationFile config)
    {
        await _importToConfigSemaphore.WaitAsync();

        try
        {
            if (_importToConfigurationFiles.TryGetValue(import, out var values))
            {
                values.Add(config);
            }
            else
            {
                _importToConfigurationFiles[import] = [config];
            }
        }
        finally
        {
            _importToConfigSemaphore.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "RunAsync with FileAndForget is ok"
    )]
    private void OnFileSave(string file)
    {
        // DocumentEvents.Saved is raised synchronously by Visual Studio. Do not wait for the
        // imports collection here, since doing so can block the UI thread during file operations.
        ThreadHelper
            .JoinableTaskFactory.RunAsync(() => OnFileSaveAsync(file))
            .FileAndForget(
                nameof(TailwindCSSIntellisense) + "/ConfigurationFileReloader/OnFileSave"
            );
    }

    private async Task OnFileSaveAsync(string file)
    {
        List<ConfigurationFile> configFiles = [];

        var configFile = _settings.ConfigurationFiles.FirstOrDefault(c =>
            c.Path.Equals(file, StringComparison.InvariantCultureIgnoreCase)
        );

        if (configFile is not null)
        {
            configFiles.Add(configFile);
        }

        await _importToConfigSemaphore.WaitAsync();
        try
        {
            if (_importToConfigurationFiles.TryGetValue(file, out var values))
            {
                configFiles.AddRange(values);
            }
        }
        finally
        {
            _importToConfigSemaphore.Release();
        }

        await Task.WhenAll(
            configFiles
                .Distinct()
                .Select(config =>
                    CompletionConfiguration.ReloadCustomAttributesAsync(config, _settings)
                )
        );
    }

    private async Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        var added = settings.ConfigurationFiles.Except(_settings.ConfigurationFiles).ToList();
        _settings = settings;

        await _importToConfigSemaphore.WaitAsync();

        try
        {
            foreach (var values in _importToConfigurationFiles.Values)
            {
                values.RemoveWhere(v => !settings.ConfigurationFiles.Contains(v));
            }
        }
        finally
        {
            _importToConfigSemaphore.Release();
        }

        if (added.Count > 0)
        {
            await CompletionConfiguration.ReloadCustomAttributesAsync(settings);
        }
    }

    public void Dispose()
    {
        VS.Events.DocumentEvents.Saved -= OnFileSave;
        SettingsProvider.OnSettingsChanged -= OnSettingsChangedAsync;
    }
}
