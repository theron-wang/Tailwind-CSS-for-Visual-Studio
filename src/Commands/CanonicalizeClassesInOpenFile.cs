using System;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using TailwindCSSIntellisense.ClassOperations;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Commands;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.CanonicalizeClassesInOpenFileCmdId)]
internal sealed class CanonicalizeClassesInOpenFile : BaseCommand<CanonicalizeClassesInOpenFile>
{
    protected override async Task InitializeCompletedAsync()
    {
        ClassOperationService = await VS.GetMefServiceAsync<ClassOperationService>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal ClassOperationService ClassOperationService { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "General settings load fast"
    )]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var settings = ThreadHelper.JoinableTaskFactory.Run(SettingsProvider.GetSettingsAsync);

        Command.Visible =
            settings.EnableTailwindCss
            && ClassOperationService
                .Operations.GetEnabledOperations(
                    settings,
                    operationType: typeof(ClassCanonicalizer)
                )
                .Any()
            && settings.BuildFiles is not null
            && settings.BuildFiles.Count > 0;
        Command.Enabled = !ClassOperationService.Processing;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (!ClassOperationService.Processing)
        {
            var file = await VS.Documents.GetActiveDocumentViewAsync();
            var path = file?.TextBuffer?.GetFileName();

            if (!string.IsNullOrWhiteSpace(path))
            {
                await ClassOperationService.ProcessAsync(
                    path!,
                    operationType: typeof(ClassCanonicalizer)
                );
            }
        }
    }
}
