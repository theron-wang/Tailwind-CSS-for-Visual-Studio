using Community.VisualStudio.Toolkit;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SetAsOutputCssFileCmdId)]
internal sealed class SetAsOutputFile : BaseCommand<SetAsOutputFile>
{
    private readonly HashSet<OleMenuCommand> _commands = [];

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

        if (!settings.EnableTailwindCss || Path.GetExtension(filePath) != ".css" || settings.BuildFiles is null || settings.BuildFiles.Count == 0 ||
            settings.BuildFiles.Any(f => f.Input.Equals(filePath, StringComparison.InvariantCultureIgnoreCase) ||
                (f.Output is not null &&
                f.Output.Equals(filePath, StringComparison.InvariantCultureIgnoreCase))))
        {
            return;
        }

        OleMenuCommandService mcs = Package.GetService<IMenuCommandService, OleMenuCommandService>();
        var i = 1;

        foreach (var command in _commands)
        {
            mcs.RemoveCommand(command);
        }

        _commands.Clear();

        SetupCommand(Command, settings.BuildFiles[0].Input, Path.GetDirectoryName(SolutionExplorerSelection.CurrentSelectedItemFullPath));

        foreach (var buildPair in settings.BuildFiles.Skip(1))
        {
            var cmdId = new CommandID(PackageGuids.guidVSPackageCmdSet, PackageIds.SetAsOutputCssFileCmdId + i++);
            var command = new OleMenuCommand(Execute, cmdId);
            SetupCommand(command, buildPair.Input, Path.GetDirectoryName(SolutionExplorerSelection.CurrentSelectedItemFullPath));
            mcs.AddCommand(command);
        }
    }

    private void SetupCommand(OleMenuCommand command, string path, string currentFolder)
    {
        command.Visible = true;
        command.Text = $"Set as output file for {PathHelpers.GetRelativePath(path, currentFolder)}";
        command.Properties["path"] = path;

        if (command != Command)
        {
            _commands.Add(command);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "No other choice + settings likely loaded by the time this command is queried")]
    protected override void Execute(object sender, EventArgs e)
    {
        var command = (OleMenuCommand)sender;

        if (command.Properties.Contains("path"))
        {
            var path = (string)command.Properties["path"];
            var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

            var buildFile = settings.BuildFiles.FirstOrDefault(b => b.Input.Equals(path, StringComparison.InvariantCultureIgnoreCase));
            var isNew = buildFile is null;

            buildFile ??= new();

            buildFile.Input = path;
            buildFile.Output = SolutionExplorerSelection.CurrentSelectedItemFullPath;

            if (isNew)
            {
                settings.BuildFiles.Add(buildFile);
            }

            ThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await SettingsProvider.OverrideSettingsAsync(settings);
            });
        }
    }
}
