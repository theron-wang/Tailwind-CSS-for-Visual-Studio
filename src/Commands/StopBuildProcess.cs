using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.StopBuildProcessCmdId)]
internal sealed class StopBuildProcess : BaseCommand<StopBuildProcess>
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
        Command.Visible = settings.EnableTailwindCss && BuildProcess.AreProcessesActive() && settings.ConfigurationFiles.Count > 0 && settings.BuildType != BuildProcessOptions.None;
        switch (settings.BuildType)
        {
            case BuildProcessOptions.Default:
            case BuildProcessOptions.ManualJIT:
                Command.Text = "Stop Tailwind CSS JIT build process";
                break;
            case BuildProcessOptions.OnBuild:
                Command.Text = "Cancel Tailwind CSS build";
                break;
            default:
                Command.Text = "Stop Tailwind CSS build process";
                break;
        }
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await BuildProcess.InitializeAsync();

        BuildProcess.EndAllProcesses();
    }
}
