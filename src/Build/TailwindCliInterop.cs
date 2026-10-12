using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Build;

/// <summary>
/// Shared command construction and process I/O for Tailwind CLI consumers.
/// Arguments can include any CLI subcommand, such as canonicalize.
/// </summary>
internal abstract class TailwindCliInterop : IDisposable
{
    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    private TailwindSettings? _currentSettings;
    protected bool AreSettingsInitialized => _currentSettings is not null;
    protected TailwindSettings _settings
    {
        get =>
            _currentSettings
            ?? throw new InvalidOperationException("Tailwind settings are not initialized.");
        set => _currentSettings = value;
    }
    private bool _settingsSubscribed;

    /// <summary>
    /// Loads the current extension settings and subscribes to subsequent changes once.
    /// </summary>
    /// <returns>A task that completes when settings have been loaded and the subscription is established.</returns>
    protected async Task InitializeSettingsAsync()
    {
        _settings = await SettingsProvider.GetSettingsAsync();

        if (!_settingsSubscribed)
        {
            SettingsProvider.OnSettingsChanged += SettingsChangedAsync;
            _settingsSubscribed = true;
        }
    }

    /// <summary>
    /// Updates the settings used to construct CLI commands.
    /// </summary>
    /// <param name="settings">The current extension settings.</param>
    /// <returns>A task that completes when the settings have been updated.</returns>
    /// <remarks>Overrides must call the base implementation to update <see cref="_settings"/>.</remarks>
    protected virtual Task SettingsChangedAsync(TailwindSettings settings)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes this consumer's settings subscription.
    /// </summary>
    /// <remarks>Overrides should also dispose their processes and call the base implementation.</remarks>
    public virtual void Dispose()
    {
        if (_settingsSubscribed)
        {
            SettingsProvider.OnSettingsChanged -= SettingsChangedAsync;
            _settingsSubscribed = false;
        }
    }

    // https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/120
    private int _encounteredSecurityError;

    /// <summary>
    /// Determines whether a process has started and has not exited or been disposed.
    /// </summary>
    /// <param name="process">The process to inspect, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the process is running; otherwise, <see langword="false"/>.</returns>
    protected virtual bool IsProcessActive(Process? process)
    {
        try
        {
            return process != null && !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            // The process may have already been disposed by its exit handler.
            return false;
        }
    }

    /// <summary>
    /// Constructs a Tailwind CLI command using the configured executable or the appropriate npx package.
    /// </summary>
    /// <param name="project">The project configuration that determines the Tailwind version.</param>
    /// <param name="args">The CLI arguments, including any subcommand and quoted argument values.</param>
    /// <param name="exit">Whether to append a command that exits the shell.</param>
    /// <param name="powershell">Whether to quote a configured CLI executable for PowerShell instead of cmd.</param>
    /// <returns>The shell command to execute.</returns>
    protected string GetCommand(
        ProjectCompletionValues project,
        string args,
        bool exit = false,
        bool powershell = false
    )
    {
        var suffix = exit ? " & exit" : "";
        if (string.IsNullOrWhiteSpace(_settings.TailwindCliPath) || !_settings.UseCli)
        {
            if (project.Version == TailwindVersion.V3)
            {
                return $"npx tailwindcss {args}{suffix}";
            }
            else
            {
                return $"npx @tailwindcss/cli {args}{suffix}";
            }
        }
        return powershell
            ? $"\"& '{_settings.TailwindCliPath}' {args.Replace('\"', '\'')}\"{suffix}"
            : $"\"\"{_settings.TailwindCliPath}\" {args}{suffix}\"";
    }

    /// <summary>
    /// Creates launch settings for a CLI command in the project configuration's directory.
    /// </summary>
    /// <param name="project">The project configuration that supplies the working directory and Tailwind version.</param>
    /// <param name="args">The CLI arguments, including any subcommand and quoted argument values.</param>
    /// <param name="powershell">Whether to use PowerShell instead of cmd.</param>
    /// <returns>Launch settings with redirected streams and the complete CLI command.</returns>
    /// <remarks>PowerShell uses the unrestricted execution policy after a security error has been encountered.</remarks>
    protected ProcessStartInfo GetCliProcessStartInfo(
        ProjectCompletionValues project,
        string args,
        bool powershell = false
    )
    {
        var startInfo = GetProcessStartInfo(Path.GetDirectoryName(project.FilePath)!, powershell);
        startInfo.Arguments = powershell
            ? $"{(Volatile.Read(ref _encounteredSecurityError) != 0 ? "-ExecutionPolicy Unrestricted" : "")} -Command "
            : "/c ";
        startInfo.Arguments += GetCommand(project, args, !powershell, powershell);
        return startInfo;
    }

    /// <summary>
    /// Creates launch settings for a hidden shell with redirected input and UTF-8 output streams.
    /// </summary>
    /// <param name="dir">The shell's working directory.</param>
    /// <param name="powershell">Whether to use PowerShell instead of cmd.</param>
    /// <returns>Shell launch settings without a command assigned.</returns>
    protected static ProcessStartInfo GetProcessStartInfo(string dir, bool powershell = false)
    {
        return new ProcessStartInfo()
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            FileName = powershell ? "powershell" : "cmd",
            WorkingDirectory = dir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    /// <summary>
    /// Starts a process and enables its exit notifications.
    /// </summary>
    /// <param name="startInfo">The shell and command launch settings.</param>
    /// <returns>The started process, which the caller is responsible for disposing.</returns>
    protected static Process StartProcess(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)!;
        process.EnableRaisingEvents = true;
        return process;
    }

    /// <summary>
    /// Starts reading both output streams and handles the PowerShell security fallback.
    /// </summary>
    /// <param name="process">The started process whose output streams should be read.</param>
    /// <param name="retryProcess">An optional callback that stops and recreates the caller's process after a security error.</param>
    /// <remarks>
    /// Output is forwarded to <see cref="OutputDataReceived"/>. The retry callback runs on an output event thread;
    /// subsequent PowerShell commands use the unrestricted policy, and replacement processes are not retried again.
    /// </remarks>
    protected void PostSetupProcess(Process process, Action? retryProcess = null)
    {
        // A process created after the fallback was enabled must not cause a retry loop,
        // even when its shell is cmd and therefore has no execution-policy argument.
        var retried = Volatile.Read(ref _encounteredSecurityError);
        var unrestricted = process.StartInfo.Arguments.Contains("-ExecutionPolicy Unrestricted");

        void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
            {
                return;
            }

            if (
                e.Data.Contains("PSSecurityException")
                && !unrestricted
                && Interlocked.Exchange(ref retried, 1) == 0
            )
            {
                if (Interlocked.Exchange(ref _encounteredSecurityError, 1) == 0)
                {
                    OnCliMessage(
                        "Tailwind CSS: Encountered a PSSecurityException, trying again with -ExecutionPolicy Unrestricted: see https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/120"
                    );
                }

                retryProcess?.Invoke();
            }

            OutputDataReceived(sender, e.Data);
        }

        // Subscribe before reading so fast commands cannot lose their first output.
        process.OutputDataReceived += OnDataReceived;
        process.ErrorDataReceived += OnDataReceived;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>
    /// Handles a line from standard output or standard error after shared security handling.
    /// </summary>
    /// <param name="sender">The process that produced the output.</param>
    /// <param name="data">The output line, without its line terminator.</param>
    protected virtual void OutputDataReceived(object sender, string data) { }

    /// <summary>
    /// Handles a diagnostic message generated by the interop layer.
    /// </summary>
    /// <param name="message">The diagnostic message to display or log.</param>
    protected virtual void OnCliMessage(string message) { }

    /// <summary>
    /// Stops an active process by sending Ctrl+C, closing its input, and terminating it.
    /// </summary>
    /// <param name="process">The process to stop.</param>
    /// <remarks>Processes that have already exited or been disposed are ignored.</remarks>
    protected void StopProcess(Process process)
    {
        if (!IsProcessActive(process))
        {
            return;
        }

        try
        {
            // Tailwind --watch keeps building even with kill; send Ctrl+C first.
            process.StandardInput.WriteLine("\x3");
            process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
            // The process may have exited or its input may already be closed.
        }
        catch (IOException)
        {
            // The process may have closed its input while exiting.
        }

        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The process may have already exited.
        }
    }
}
