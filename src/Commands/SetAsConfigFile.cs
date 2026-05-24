using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SetAsConfigFileCmdId)]
internal sealed class SetAsConfigFile : BaseCommand<SetAsConfigFile>
{
    protected override async Task InitializeCompletedAsync()
    {
        SolutionExplorerSelection = await VS.GetMefServiceAsync<SolutionExplorerSelectionService>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
        DirectoryVersionFinder = await VS.GetMefServiceAsync<DirectoryVersionFinder>();
    }

    internal SolutionExplorerSelectionService SolutionExplorerSelection { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;
    internal DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "No other choice + settings likely loaded by the time this command is queried")]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var filePath = SolutionExplorerSelection.CurrentSelectedItemFullPath;

        var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

        Command.Visible = settings.EnableTailwindCss;

        if (!Command.Visible)
        {
            return;
        }

        var version = ThreadHelper.JoinableTaskFactory.Run(() => DirectoryVersionFinder.GetTailwindVersionAsync(filePath, settings));

        if (version == TailwindVersion.V3)
        {
            Command.Visible = !settings.ConfigurationFiles.Any(c => c.Path.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
                && DefaultConfigurationFileNames.Extensions.Contains(Path.GetExtension(filePath));
        }
        else
        {
            Command.Visible = !settings.BuildFiles.Any(b => b.Input.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))
                && Path.GetExtension(filePath) == ".css";
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        var settings = await SettingsProvider.GetSettingsAsync();

        var path = SolutionExplorerSelection.CurrentSelectedItemFullPath;

        if (Path.GetExtension(path) == ".css")
        {
            settings.BuildFiles.Add(new() { Input = path });
        }
        else
        {
            settings.ConfigurationFiles.Add(new() { Path = path });
        }

        await SettingsProvider.OverrideSettingsAsync(settings);
    }
}
