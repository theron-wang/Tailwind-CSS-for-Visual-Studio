using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SetAsOutputCssFileMenu)]
internal sealed class SetAsOutputFileMenu : BaseCommand<SetAsOutputFileMenu>
{
    protected override async Task InitializeCompletedAsync()
    {
        SolutionExplorerSelection = await VS.GetMefServiceAsync<SolutionExplorerSelectionService>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal SolutionExplorerSelectionService SolutionExplorerSelection { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "No other choice + settings likely loaded by the time this command is queried"
    )]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var filePath = SolutionExplorerSelection.CurrentSelectedItemFullPath;

        var settings = Package.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

        Command.Visible =
            settings.EnableTailwindCss
            && Path.GetExtension(filePath) == ".css"
            && settings.BuildFiles is not null
            && settings.BuildFiles.Count > 0
            && settings.BuildFiles.All(f =>
                !f.Input.Equals(filePath, StringComparison.InvariantCultureIgnoreCase)
                && (
                    f.Output is null
                    || !f.Output.Equals(filePath, StringComparison.InvariantCultureIgnoreCase)
                )
            );
    }
}
