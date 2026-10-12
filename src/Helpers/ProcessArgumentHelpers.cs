using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TailwindCSSIntellisense.Helpers;

internal static class ProcessArgumentHelpers
{
    public static ProcessStartInfo CreateNodeProcessStartInfo(
        string workingDirectory,
        params string[] arguments
    )
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "cmd",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Keep the cmd wrapper for Visual Studio's Node launch environment. Expanded
        // variable values are not expanded again; disable delayed expansion for '!'.
        var command = new StringBuilder("/d /v:off /c node");
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            // Quotes/newlines cannot occur in Windows file names and would break
            // shell quoting. Send JavaScript source through stdin instead.
            if (argument.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            {
                throw new ArgumentException(
                    "Node arguments cannot contain quotes or control characters.",
                    nameof(arguments)
                );
            }

            var variable = "TAILWIND_NODE_ARG_" + i;
            processInfo.EnvironmentVariables[variable] = QuoteWindowsArgument(argument);
            command.Append(" %").Append(variable).Append('%');
        }
        processInfo.Arguments = command.ToString();
        return processInfo;
    }

    // Quote for Node's Windows argument parser after the shell-safe checks above.
    private static string QuoteWindowsArgument(string value)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                result.Append('\\', backslashes);
            }
            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }

    public static bool IsNpmPackageVersion(string value)
    {
        return value.Length <= 256
            && Regex.IsMatch(
                value,
                @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z"
            );
    }
}
