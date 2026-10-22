using System.Diagnostics;
using System.Text.Json;
using TailwindCSSIntellisense.Helpers;

namespace TailwindCSSIntellisense.Tests.IntegrationTests;

[Collection("External processes")]
public class NodeProcessArgumentsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CmdNodeLaunch_PreservesArgumentsWithoutShellExpansion(bool useStandardInput)
    {
        string[] arguments =
        [
            "",
            "config with spaces.js",
            "config&echo injected.js",
            "%PATH% !PATH! ^ | < >",
            "C:\\directory with spaces\\",
            "日本語.config.js",
        ];
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"node-test-&%PATH%-日本語-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directory);
        try
        {
            var scriptPath = Path.Combine(directory, "parser & %PATH%.js");
            var script = "console.log(JSON.stringify(process.argv.slice(2)))";
            await File.WriteAllTextAsync(scriptPath, script);
            var startInfo = ProcessArgumentHelpers.CreateNodeProcessStartInfo(
                directory,
                [useStandardInput ? "-" : scriptPath, .. arguments]
            );
            Assert.Equal("cmd", startInfo.FileName);
            using var process = Process.Start(startInfo)!;
            if (useStandardInput)
            {
                await process.StandardInput.WriteLineAsync(script);
            }
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, error);
            Assert.Equal(arguments, JsonSerializer.Deserialize<string[]>(output));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
