using System.Diagnostics;
using TailwindCSSIntellisense.Build;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Tests.UnitTests;

public class TailwindCliInteropTests
{
    [Theory]
    [InlineData(
        TailwindVersion.V3,
        "-i input.css -o output.css",
        "npx tailwindcss -i input.css -o output.css"
    )]
    [InlineData(
        TailwindVersion.V4,
        "-i input.css -o output.css",
        "npx @tailwindcss/cli -i input.css -o output.css"
    )]
    [InlineData(
        TailwindVersion.V4_2,
        "canonicalize --help",
        "npx @tailwindcss/cli canonicalize --help"
    )]
    public void Command_SupportsBuildAndOtherSubcommands(
        TailwindVersion version,
        string args,
        string expected
    )
    {
        var interop = new TestInterop(new TailwindSettings());

        Assert.Equal(expected, interop.Command(version, args));
    }

    [Theory]
    [InlineData(
        false,
        "\"\"C:\\Tailwind CLI\\tailwindcss.exe\" canonicalize -i \"input.css\" & exit\""
    )]
    [InlineData(true, "\"& 'C:\\Tailwind CLI\\tailwindcss.exe' canonicalize -i 'input.css'\"")]
    public void Command_PreservesCustomCliPathQuoting(bool powershell, string expected)
    {
        var interop = new TestInterop(
            new TailwindSettings
            {
                UseCli = true,
                TailwindCliPath = @"C:\Tailwind CLI\tailwindcss.exe",
            }
        );

        Assert.Equal(
            expected,
            interop.Command(
                TailwindVersion.V4_2,
                "canonicalize -i \"input.css\"",
                !powershell,
                powershell
            )
        );
    }

    [Fact]
    public void Command_UsesNpxWhenCustomCliIsDisabled()
    {
        var interop = new TestInterop(
            new TailwindSettings { UseCli = false, TailwindCliPath = @"C:\tailwindcss.exe" }
        );

        Assert.Equal(
            "npx @tailwindcss/cli canonicalize & exit",
            interop.Command(TailwindVersion.V4_2, "canonicalize", exit: true)
        );
    }

    [Theory]
    [InlineData(false, "cmd", "/c npx @tailwindcss/cli canonicalize & exit")]
    [InlineData(true, "powershell", " -Command npx @tailwindcss/cli canonicalize")]
    public void StartInfo_UsesProjectDirectoryAndSelectedShell(
        bool powershell,
        string shell,
        string arguments
    )
    {
        var interop = new TestInterop(new TailwindSettings());
        var startInfo = interop.CliStartInfo("canonicalize", powershell);

        Assert.Equal(
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            startInfo.WorkingDirectory
        );
        Assert.Equal(shell, startInfo.FileName);
        Assert.Equal(arguments, startInfo.Arguments);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public async Task InitializeSettings_LoadsCurrentSettingsWithoutDuplicateSubscriptions()
    {
        var provider = new SettingsProvider
        {
            Settings = new TailwindSettings
            {
                UseCli = true,
                TailwindCliPath = @"C:\Tailwind CLI\tailwindcss.exe",
            },
        };
        using var interop = new TestInterop(new TailwindSettings()) { SettingsProvider = provider };

        await interop.InitializeSettingsAsyncForTest();
        Assert.Contains(
            provider.Settings.TailwindCliPath!,
            interop.Command(TailwindVersion.V4_2, "canonicalize")
        );

        provider.Settings = new TailwindSettings();
        await interop.InitializeSettingsAsyncForTest();

        Assert.Single(provider.OnSettingsChanged!.GetInvocationList());
        Assert.Equal(
            "npx @tailwindcss/cli canonicalize",
            interop.Command(TailwindVersion.V4_2, "canonicalize")
        );
    }

    [Fact]
    public async Task SettingsChanges_UpdateCommandsAndAllowHookToInspectPreviousSettings()
    {
        var provider = new SettingsProvider();
        using var interop = new TestInterop(new TailwindSettings()) { SettingsProvider = provider };
        await interop.InitializeSettingsAsyncForTest();
        var updated = new TailwindSettings
        {
            UseCli = true,
            TailwindCliPath = @"C:\Tailwind CLI\tailwindcss.exe",
        };

        await provider.OnSettingsChanged!(updated);

        Assert.Same(provider.Settings, interop.SettingsBeforeChange);
        Assert.Contains(
            updated.TailwindCliPath!,
            interop.Command(TailwindVersion.V4_2, "canonicalize")
        );
    }

    [Fact]
    public async Task Dispose_RemovesOnlyItsOwnSettingsSubscription()
    {
        var provider = new SettingsProvider();
        Func<TailwindSettings, Task> otherSubscriber = _ => Task.CompletedTask;
        provider.OnSettingsChanged += otherSubscriber;
        var interop = new TestInterop(new TailwindSettings()) { SettingsProvider = provider };
        await interop.InitializeSettingsAsyncForTest();

        interop.Dispose();
        interop.Dispose();
        await provider.OnSettingsChanged!(
            new TailwindSettings { UseCli = true, TailwindCliPath = @"C:\tailwindcss.exe" }
        );

        Assert.Same(otherSubscriber, Assert.Single(provider.OnSettingsChanged.GetInvocationList()));
        Assert.Null(interop.SettingsBeforeChange);
        Assert.Equal(
            "npx @tailwindcss/cli canonicalize",
            interop.Command(TailwindVersion.V4_2, "canonicalize")
        );
    }

    private sealed class TestInterop : TailwindCliInterop
    {
        public TestInterop(TailwindSettings settings)
        {
            _settings = settings;
        }

        public TailwindSettings? SettingsBeforeChange { get; private set; }

        public Task InitializeSettingsAsyncForTest() => InitializeSettingsAsync();

        protected override Task SettingsChangedAsync(TailwindSettings settings)
        {
            SettingsBeforeChange = _settings;
            return base.SettingsChangedAsync(settings);
        }

        public string Command(
            TailwindVersion version,
            string args,
            bool exit = false,
            bool powershell = false
        )
        {
            return GetCommand(
                new ProjectCompletionValues { Version = version },
                args,
                exit,
                powershell
            );
        }

        public ProcessStartInfo CliStartInfo(string args, bool powershell)
        {
            return GetCliProcessStartInfo(
                new ProjectCompletionValues
                {
                    Version = TailwindVersion.V4_2,
                    FilePath = Path.Combine(Path.GetTempPath(), "input.css"),
                },
                args,
                powershell
            );
        }
    }
}
