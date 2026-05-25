using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Helpers;

internal class ClassRegexHelper
{
    // To get the match value, get capture group 'content'
    // https://regex101.com/r/Odcyjx/3
    private static readonly Regex _classRegex = new(@"[cC]lass\s*=\s*(['""])\s*(?<content>(?:\n.|(?!\1).)*)?\s*(\1|$)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex _javaScriptClassRegex = new(@"[cC]lassName\s*=\s*(['""])\s*(?<content>(?:\n.|(?!\1).)*)?\s*(\1|$)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex _razorClassRegex = new(@"[cC]lass(?:es)?\s*=\s*([""'])(?<content>(?:[^""'\\@]|\\.|@(?:[a-zA-Z0-9.]+)?\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|(?:(?!\1)[^\\]|\\$|\\.)|\([^)]*\))*)(\1|$)", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex _razorSplitClassRegex = new(@"(?:(?:@@|@\(""@""\)|[^\s@])+|@[\w.]*\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|@[\w.]+)+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex _splitClassRegex = new(@"([^\s]+)", RegexOptions.Compiled);

    // For use on the content capture group of class regexes. No match if there are no single quote pairs.
    // For example, in open ? 'hi @('h')' : '@(Model.Name)', the matches would be 'hi @('h')' and '@(Model.Name)'
    // This is mainly used to support libraries like Alpine.JS, which use single quotes of classes within class attributes.
    // We avoid selecting single quote pairs within square brackets, though, since that could represent an arbitrary attribute
    // or class (like before:content-[''])
    private static readonly Regex _razorQuotePairRegex = new(@"(?<!@\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))(?<!\[\s*)'(?<content>[^\]](?:@\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|(?:(?!')[^\\]|\\.))*)'", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex _normalQuotePairRegex = new(@"(?<!\[\s*)'(?<content>[^\]](?:[^'\\]|\\.)*)'", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static List<Regex>? _customRazorRegexes;
    private static List<Regex>? _customNormalRegexes;
    private static List<Regex>? _customJavaScriptRegexes;

    private static bool _overrideRazor;
    private static bool _overrideNormal;
    private static bool _overrideJavaScript;

    private static TailwindSettings? _settingsCache;
    public static Func<Task<TailwindSettings>>? GetTailwindSettings;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously", Justification = "GetTailwindSettings is only non-null when some sort of settings is already cached")]
    private static void UpdateTailwindSettingsIfNeeded()
    {
        if (GetTailwindSettings is not null)
        {
            var settings = ThreadHelper.JoinableTaskFactory.Run(GetTailwindSettings);

            if (settings != _settingsCache)
            {
                _settingsCache = settings;
                SetCustomRegex(settings?.CustomRegexes);
            }
        }
    }

    private static void SetCustomRegex(CustomRegexes? custom)
    {
        if (custom is null)
        {
            _customRazorRegexes = null;
            _customNormalRegexes = null;
            _customJavaScriptRegexes = null;
            _overrideRazor = false;
            _overrideNormal = false;
            _overrideJavaScript = false;
            return;
        }

        try
        {
            if (custom.Razor is not null)
            {
                _overrideRazor = custom.Razor.Override;
                _customRazorRegexes = [];

                foreach (var value in custom.Razor.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled);

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError($"Invalid custom regex: {value}. The regex must contain a capture group named 'content'.");
                        ResetCustomRegex();
                        return;
                    }

                    _customRazorRegexes.Add(newRegex);
                }
            }

            if (custom.HTML is not null)
            {
                _overrideNormal = custom.HTML.Override;
                _customNormalRegexes = [];

                foreach (var value in custom.HTML.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled);

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError($"Invalid custom regex: {value}. The regex must contain a capture group named 'content'.");
                        ResetCustomRegex();
                        return;
                    }

                    _customNormalRegexes.Add(newRegex);
                }
            }

            if (custom.JavaScript is not null)
            {
                _overrideJavaScript = custom.JavaScript.Override;
                _customJavaScriptRegexes = [];

                foreach (var value in custom.JavaScript.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled);

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError($"Invalid custom regex: {value}. The regex must contain a capture group named 'content'.");
                        ResetCustomRegex();
                        return;
                    }

                    _customJavaScriptRegexes.Add(newRegex);
                }
            }

            return;
        }
        catch (Exception ex)
        {
            ex.Log("Tailwind CSS: Setting custom regex failed.");
            VS.MessageBox.ShowError("Invalid custom regex. Please check the output window for more details.");
            ResetCustomRegex();
        }
    }

    private static void ResetCustomRegex()
    {
        _customRazorRegexes = null;
        _customNormalRegexes = null;
        _customJavaScriptRegexes = null;
        _overrideRazor = false;
        _overrideNormal = false;
        _overrideJavaScript = false;
    }

    /// <summary>
    /// Gets all class matches in a normal HTML context.
    /// Includes: class="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesNormal(string text, string expandedSearchText)
    {
        UpdateTailwindSettingsIfNeeded();

        IEnumerable<Match> matches;
        if (!_overrideNormal || _customNormalRegexes is null || _customNormalRegexes.Count == 0)
        {
            matches = ((_customNormalRegexes?.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>())) ?? [])
                .Concat(_classRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = _customNormalRegexes.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>());
        }

        return matches.SelectMany(match =>
        {
            var classContent = GetClassTextGroup(match);

            if (_normalQuotePairRegex.IsMatch(classContent.Value))
            {
                var lastQuoteMatchIndex = classContent.Index;

                List<Match> pairs = [];

                while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Max(0, classContent.Index + classContent.Length - lastQuoteMatchIndex)) is Match quoteMatch && quoteMatch.Success)
                {
                    lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                    pairs.Add(quoteMatch);
                }

                return pairs;
            }

            return [match];
        });
    }

    /// <summary>
    /// Gets all class matches in a razor context.
    /// Includes: class="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesRazor(string text, string expandedSearchText)
    {
        UpdateTailwindSettingsIfNeeded();

        IEnumerable<Match> matches;
        if (!_overrideRazor || _customRazorRegexes is null || _customRazorRegexes.Count == 0)
        {
            matches = ((_customRazorRegexes?.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>())) ?? [])
                .Concat(_razorClassRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = _customRazorRegexes.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>());
        }

        return matches.SelectMany(match =>
        {
            var classContent = GetClassTextGroup(match);

            var razorSyntaxLocations = _razorSplitClassRegex.Matches(classContent.Value).Cast<Match>().Where(match => match.Value.Length > 0 && match.Value[0] == '@');

            if (_razorQuotePairRegex.IsMatch(classContent.Value))
            {
                var lastQuoteMatchIndex = classContent.Index;

                List<Match> pairs = [];

                while (_razorQuotePairRegex.Match(expandedSearchText, lastQuoteMatchIndex, Math.Max(0, classContent.Index + classContent.Length - lastQuoteMatchIndex)) is Match quoteMatch && quoteMatch.Success)
                {
                    lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;

                    var localIndex = quoteMatch.Index - GetClassTextGroup(match).Index;

                    // If this quote pair match is inside a razor block, we also want to ignore it
                    if (!razorSyntaxLocations.Any(r => r.Index <= localIndex && r.Index + r.Length >= localIndex))
                    {
                        pairs.Add(quoteMatch);
                    }
                }

                return pairs;
            }

            return [match];
        });
    }

    /// <summary>
    /// Gets all class matches in a JS context, such as with React.
    /// Includes: className="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesJavaScript(string text, string expandedSearchText)
    {
        UpdateTailwindSettingsIfNeeded();

        IEnumerable<Match> matches;
        if (!_overrideJavaScript || _customJavaScriptRegexes is null || _customJavaScriptRegexes.Count == 0)
        {
            matches = ((_customJavaScriptRegexes?.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>())) ?? [])
                .Concat(_javaScriptClassRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = _customJavaScriptRegexes.SelectMany(regex => regex.Matches(expandedSearchText).Cast<Match>());
        }

        return matches.SelectMany(match =>
        {
            var classContent = GetClassTextGroup(match);

            if (_normalQuotePairRegex.IsMatch(classContent.Value))
            {
                var lastQuoteMatchIndex = classContent.Index;

                List<Match> pairs = [];

                while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Max(0, classContent.Index + classContent.Length - lastQuoteMatchIndex)) is Match quoteMatch && quoteMatch.Success)
                {
                    lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                    pairs.Add(quoteMatch);
                }

                return pairs;
            }

            return [match];
        });
    }

    /// <summary>
    /// Gets all class matches in a razor context.
    /// Includes: class="...". This specific method uses a yield return method.
    /// </summary>
    /// <remarks>
    /// This method does not guarantee that matches are ordered sequentially.
    /// </remarks>
    public static IEnumerable<Match> GetClassesRazorEnumerator(string text)
    {
        UpdateTailwindSettingsIfNeeded();

        foreach (var customRegex in _customRazorRegexes ?? [])
        {
            var lastMatchIndex = 0;
            while (customRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_razorQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (_razorQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }
                yield return match;
            }
        }

        if (!_overrideRazor || _customRazorRegexes is null || _customRazorRegexes.Count == 0)
        {
            var lastMatchIndex = 0;

            while (_razorClassRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                var razorSyntaxLocations = _razorSplitClassRegex.Matches(classText).Cast<Match>().Where(match => match.Value.Length > 0 && match.Value[0] == '@');

                if (_razorQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (_razorQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;

                        var localIndex = quoteMatch.Index - GetClassTextGroup(match).Index;

                        // If this quote pair match is inside a razor block, we also want to ignore it
                        if (!razorSyntaxLocations.Any(r => r.Index <= localIndex && r.Index + r.Length >= localIndex))
                        {
                            yield return quoteMatch;
                        }
                    }
                    continue;
                }

                yield return match;
            }
        }

        yield break;
    }

    /// <summary>
    /// Gets all class matches in a normal context.
    /// Includes: class="...". This specific method uses a yield return method.
    /// </summary>
    /// <remarks>
    /// This method does not guarantee that matches are ordered sequentially.
    /// </remarks>
    public static IEnumerable<Match> GetClassesNormalEnumerator(string text)
    {
        UpdateTailwindSettingsIfNeeded();

        if (!_overrideNormal || _customNormalRegexes is null || _customNormalRegexes.Count == 0)
        {
            var lastMatchIndex = 0;

            while (_classRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }

                yield return match;
            }
        }

        foreach (var customRegex in _customNormalRegexes ?? [])
        {
            var lastMatchIndex = 0;
            while (customRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }
                yield return match;
            }
        }

        yield break;
    }

    /// <summary>
    /// Gets all class matches in a normal context.
    /// Includes: className="...". This specific method uses a yield return method.
    /// </summary>
    /// <remarks>
    /// This method does not guarantee that matches are ordered sequentially.
    /// </remarks>
    public static IEnumerable<Match> GetClassesJavaScriptEnumerator(string text)
    {
        UpdateTailwindSettingsIfNeeded();

        if (!_overrideJavaScript || _customJavaScriptRegexes is null || _customJavaScriptRegexes.Count == 0)
        {
            var lastMatchIndex = 0;

            while (_javaScriptClassRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }

                yield return match;
            }
        }

        foreach (var customRegex in _customJavaScriptRegexes ?? [])
        {
            var lastMatchIndex = 0;
            while (customRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (_normalQuotePairRegex.Match(text, lastQuoteMatchIndex, Math.Min(text.Length - lastQuoteMatchIndex, match.Length)) is Match quoteMatch && quoteMatch.Success)
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }
                yield return match;
            }
        }

        yield break;
    }

    /// <summary>
    /// Splits the razor class attribute into individual classes; should be called on each Match
    /// from GetClassesRazor
    /// </summary>
    /// <param name="text">An individual razor class context (the ... in class="...")</param>
    public static IEnumerable<Match> SplitRazorClasses(string text)
    {
        var matches = _razorSplitClassRegex.Matches(text).Cast<Match>();
        return matches;
    }

    /// <summary>
    /// Splits the class attribute into individual classes; should be called on each Match
    /// from GetClassesRazor
    /// </summary>
    /// <param name="text">An individual razor class context (the ... in class="...")</param>
    public static IEnumerable<Match> SplitNonRazorClasses(string text)
    {
        var matches = _splitClassRegex.Matches(text).Cast<Match>();
        return matches;
    }

    public static Group GetClassTextGroup(Match match)
    {
        // 'content' capture group matches the class value
        return match.Groups["content"];
    }
}
