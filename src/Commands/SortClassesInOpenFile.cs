using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Threading.Tasks;
using TailwindCSSIntellisense.ClassSort;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense;

[Command(PackageGuids.guidVSPackageCmdSetString, PackageIds.SortInOpenFileCmdId)]
internal sealed class SortClassesInOpenFile : BaseCommand<SortClassesInOpenFile>
{
    protected override async Task InitializeCompletedAsync()
    {
        ClassSorter = await VS.GetMefServiceAsync<ClassSorter>();
        SettingsProvider = await VS.GetMefServiceAsync<SettingsProvider>();
    }

    internal ClassSorter ClassSorter { get; set; } = null!;
    internal SettingsProvider SettingsProvider { get; set; } = null!;


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "General settings load fast")]
    protected override void BeforeQueryStatus(EventArgs e)
    {
        var settings = ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

        Command.Visible = settings.UseTailwindCss && settings.ClassSortType != SortClassesOptions.None;
        Command.Enabled = !ClassSorter.Sorting;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        if (!ClassSorter.Sorting)
        {
            var file = await VS.Documents.GetActiveDocumentViewAsync();
            var path = file?.TextBuffer?.GetFileName();

            if (!string.IsNullOrWhiteSpace(path))
            {
                await ClassSorter.SortAsync(path!, true);
            }
        }
    }
}
