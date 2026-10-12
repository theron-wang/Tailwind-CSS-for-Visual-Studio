namespace TailwindCSSIntellisense.Configuration;

// Mirrors the production service's API so the scanner can be tested without Visual Studio.
public sealed class FileFinder
{
    internal List<string> CssFiles { get; } = [];
    internal List<string> JavascriptFiles { get; } = [];

    internal Task<List<string>> GetCssFilesAsync() => Task.FromResult(CssFiles);

    internal Task<List<string>> GetJavascriptFilesAsync() => Task.FromResult(JavascriptFiles);
}
