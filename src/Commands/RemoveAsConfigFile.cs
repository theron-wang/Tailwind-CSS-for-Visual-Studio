using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.RemoveAsConfigFileCmdId)]
internal sealed class RemoveAsConfigFile : BaseCommand<RemoveAsConfigFile>
{
    protected override async Task InitializeCompletedAsync()
    {
        SolutionExplorerSelection = await VS.GetMefServiceAsync<SolutionExplorerSelectionService>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal SolutionExplorerSelectionService SolutionExplorerSelection { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "No other choice + settings likely loaded by the time this command is queried")]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var filePath = SolutionExplorerSelection.CurrentSelectedItemFullPath;

        var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

        Command.Visible = settings.EnableTailwindCss && settings.ConfigurationFiles.Any(c => c.Path.Equals(filePath, StringComparison.InvariantCultureIgnoreCase));
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        var settings = await SettingsProvider.GetSettingsAsync();

        var filePath = SolutionExplorerSelection.CurrentSelectedItemFullPath;

        settings.ConfigurationFiles.RemoveAll(c => c.Path.Equals(filePath, StringComparison.InvariantCultureIgnoreCase));

        if (Path.GetExtension(filePath) == ".css")
        {
            settings.BuildFiles.RemoveAll(f => f.Input.Equals(filePath, StringComparison.InvariantCultureIgnoreCase));
        }

        await SettingsProvider.OverrideSettingsAsync(settings);
    }
}
