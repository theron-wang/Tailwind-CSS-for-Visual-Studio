using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using TailwindCSSIntellisense.Completions;
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
    public async Task InitializeAsync()
    {
        _settings = await SettingsProvider.GetSettingsAsync();
        if (!_subscribed)
        {
            VS.Events.DocumentEvents.Saved += OnFileSave;
            SettingsProvider.OnSettingsChanged += OnSettingsChangedAsync;

            _subscribed = true;
        }

        await CompletionConfiguration.ReloadCustomAttributesAsync(_settings);
    }

    internal async Task ReplaceAllImportsAsync(IEnumerable<ProjectCompletionValues> projects)
    {
        await _importToConfigSemaphore.WaitAsync();
        try
        {
            _importToConfigurationFiles.Clear();
            foreach (var project in projects)
            {
                foreach (var import in project.LoadedConfiguration?.Imports ?? [])
                {
                    if (!_importToConfigurationFiles.TryGetValue(import, out var values))
                        _importToConfigurationFiles[import] = values = [];
                    values.Add(new() { Path = project.FilePath });
                }
            }
        }
        finally
        {
            _importToConfigSemaphore.Release();
        }
    }

    internal async Task ReplaceImportsAsync(ConfigurationFile config, IEnumerable<string> imports)
    {
        await _importToConfigSemaphore.WaitAsync();

        try
        {
            foreach (var values in _importToConfigurationFiles.Values)
            {
                values.RemoveWhere(v =>
                    v.Path.Equals(config.Path, StringComparison.OrdinalIgnoreCase)
                );
            }
            foreach (
                var key in _importToConfigurationFiles
                    .Where(p => p.Value.Count == 0)
                    .Select(p => p.Key)
                    .ToArray()
            )
                _importToConfigurationFiles.Remove(key);
            foreach (var import in imports)
            {
                if (!_importToConfigurationFiles.TryGetValue(import, out var values))
                    _importToConfigurationFiles[import] = values = [];
                values.Add(config);
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
                .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(config =>
                    CompletionConfiguration.ReloadCustomAttributesAsync(config, _settings)
                )
        );
    }

    private async Task OnSettingsChangedAsync(TailwindSettings settings)
    {
        var added = settings
            .ConfigurationFiles.Where(f =>
                !_settings.ConfigurationFiles.Any(old =>
                    old.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
        _settings = settings;

        await _importToConfigSemaphore.WaitAsync();

        try
        {
            foreach (var values in _importToConfigurationFiles.Values)
            {
                values.RemoveWhere(v =>
                    !settings.ConfigurationFiles.Any(f =>
                        f.Path.Equals(v.Path, StringComparison.OrdinalIgnoreCase)
                    )
                );
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
