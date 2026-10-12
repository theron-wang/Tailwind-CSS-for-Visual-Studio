namespace TailwindCSSIntellisense.Settings;

public sealed class SettingsProvider
{
    public TailwindSettings Settings { get; set; } = new();

    public Func<TailwindSettings, Task>? OnSettingsChanged;

    public Task<TailwindSettings> GetSettingsAsync() => Task.FromResult(Settings);
}
