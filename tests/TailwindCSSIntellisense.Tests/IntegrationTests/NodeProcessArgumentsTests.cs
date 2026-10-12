using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TailwindCSSIntellisense.Helpers;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

public class NodeProcessArgumentsTests
{
    [Fact]
    public async Task DirectNodeLaunch_PreservesArgumentsWithoutShellExpansion()
    {
        string[] arguments =
        [
            "",
            "config with spaces.js",
            "config&echo injected.js",
            "%PATH% !PATH! ^ | < >",
            "quotes\"and\\slashes",
            "C:\\directory with spaces\\",
            "C:\\directory\\\"quoted\"\\",
            "日本語.config.js",
        ];
        var script = "console.log(JSON.stringify(process.argv.slice(1)))";
        using var process = Process.Start(
            new ProcessStartInfo
            {
                FileName = "node",
                Arguments =
                    "-e "
                    + ProcessArgumentHelpers.QuoteWindowsArgument(script)
                    + " "
                    + string.Join(
                        " ",
                        arguments.Select(ProcessArgumentHelpers.QuoteWindowsArgument)
                    ),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            }
        )!;

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, error);
        Assert.Equal(arguments, JsonSerializer.Deserialize<string[]>(output));
    }
}
