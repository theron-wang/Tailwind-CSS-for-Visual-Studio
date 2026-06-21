using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using TailwindCSSIntellisense.Node;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SetUpTailwindCliCmdId)]
internal sealed class SetUpAndUseTailwindCli : BaseCommand<SetUpAndUseTailwindCli>
{
    protected override async Task InitializeCompletedAsync()
    {
        SolutionExplorerSelection = await VS.GetMefServiceAsync<SolutionExplorerSelectionService>();
        TailwindSetUpProcess = await VS.GetMefServiceAsync<TailwindSetUpProcess>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal SolutionExplorerSelectionService SolutionExplorerSelection { get; set; } = null!;
    internal TailwindSetUpProcess TailwindSetUpProcess { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "General settings load fast"
    )]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var settings = ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

        Command.Visible = File.Exists(settings.TailwindCliPath);
        Command.Enabled = !TailwindSetUpProcess.IsSettingUp;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (!TailwindSetUpProcess.IsSettingUp)
        {
            var directory = Path.GetDirectoryName(
                SolutionExplorerSelection.CurrentSelectedItemFullPath
            );
            // Check again to see if there were any changes since the last settings cache
            // User may have manually run the setup command, for example
            SettingsProvider.RefreshSettings();
            var settings = await SettingsProvider.GetSettingsAsync();

            var hasConfig =
                settings.ConfigurationFiles.Count > 0
                && settings.ConfigurationFiles.Any(c =>
                    !string.IsNullOrWhiteSpace(c.Path) && File.Exists(c.Path)
                );

            var configFile = await TailwindSetUpProcess.RunAsync(directory, false, !hasConfig);

            if (string.IsNullOrWhiteSpace(configFile) || !File.Exists(configFile))
            {
                return;
            }

            settings.ConfigurationFiles.Add(new() { Path = configFile! });
            settings.BuildFiles.Add(new() { Input = configFile! });
            settings.UseCli = true;
            await SettingsProvider.OverrideSettingsAsync(settings, directory);

            var file = await PhysicalFile.FromFileAsync(
                SolutionExplorerSelection.CurrentSelectedItemFullPath
            );

            if (file is not null && file.ContainingProject is not null)
            {
                // tailwind.extension.json is placed in the same directory as tailwind.css
                var tailwindExtensionJson = await SettingsProvider.GetFilePathAsync();
                await file.ContainingProject.AddExistingFilesAsync(
                    configFile!,
                    tailwindExtensionJson
                );
            }
        }
    }
}
