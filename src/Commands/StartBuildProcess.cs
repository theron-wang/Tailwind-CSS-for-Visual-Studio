using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.StartBuildProcessCmdId)]
internal sealed class StartBuildProcess : BaseCommand<StartBuildProcess>
{
    protected override async Task InitializeCompletedAsync()
    {
        BuildProcess = await VS.GetMefServiceAsync<TailwindBuildProcess>();
        ConfigFileScanner = await VS.GetMefServiceAsync<ConfigFileScanner>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal TailwindBuildProcess BuildProcess { get; set; } = null!;
    internal ConfigFileScanner ConfigFileScanner { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "No other choice + settings likely loaded by the time this command is queried")]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

        Command.Visible = settings.EnableTailwindCss && BuildProcess.AreProcessesActive() == false && settings.ConfigurationFiles.Count > 0 && settings.BuildType != BuildProcessOptions.None;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await BuildProcess.InitializeAsync();

        await BuildProcess.BuildAllAsync(BuildBehavior.Default);
    }
}
