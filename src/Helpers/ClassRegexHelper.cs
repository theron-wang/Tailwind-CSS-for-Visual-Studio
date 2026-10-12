using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Helpers;

internal class ClassRegexHelper
{
    // To get the match value, get capture group 'content'
    // https://regex101.com/r/Odcyjx/3
    private static readonly Regex _cssRegex = new(
        @"@apply\s+(?<content>[^;}]+?)\s*[;}]",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _classRegex = new(
        @"[cC]lass\s*=\s*(['""])\s*(?<content>(?:\n.|(?!\1).)*)?\s*(\1|$)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _javaScriptClassRegex = new(
        @"[cC]lassName\s*=\s*(['""])\s*(?<content>(?:\n.|(?!\1).)*)?\s*(\1|$)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _razorClassRegex = new(
        @"[cC]lass(?:es)?\s*=\s*([""'])(?<content>(?:[^""'\\@]|\\.|@(?:[a-zA-Z0-9.]+)?\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|(?:(?!\1)[^\\]|\\$|\\.)|\([^)]*\))*)(\1|$)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _razorSplitClassRegex = new(
        @"(?:(?:@@|@\(""@""\)|[^\s@])+|@[\w.]*\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|@[\w.]+)+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _splitClassRegex = new(@"([^\s]+)", RegexOptions.Compiled);

    // For use on the content capture group of class regexes. No match if there are no single quote pairs.
    // For example, in open ? 'hi @('h')' : '@(Model.Name)', the matches would be 'hi @('h')' and '@(Model.Name)'
    // This is mainly used to support libraries like Alpine.JS, which use single quotes of classes within class attributes.
    // We avoid selecting single quote pairs within square brackets, though, since that could represent an arbitrary attribute
    // or class (like before:content-[''])
    private static readonly Regex _razorQuotePairRegex = new(
        @"(?<!@\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!)))(?<!\[\s*)'(?<content>[^\]](?:@\((?>\((?<c>)|[^()]+|\)(?<-c>))*(?(c)(?!))\)|(?:(?!')[^\\]|\\.))*)'",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex _normalQuotePairRegex = new(
        @"(?<!\[\s*)'(?<content>[^\]](?:[^'\\]|\\.)*)'",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1)
    );

    // Build privately and publish once; readers retain a complete settings snapshot.
    private sealed class RegexConfiguration(TailwindSettings? settings)
    {
        public TailwindSettings? Settings { get; } = settings;
        public List<Regex>? Razor;
        public List<Regex>? Normal;
        public List<Regex>? JavaScript;
        public bool OverrideRazor;
        public bool OverrideNormal;
        public bool OverrideJavaScript;
    }

    private static RegexConfiguration _regexConfiguration = new(null);
    public static Func<Task<TailwindSettings>>? GetTailwindSettings;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "GetTailwindSettings is only non-null when some sort of settings is already cached"
    )]
    private static RegexConfiguration GetRegexConfiguration()
    {
        var getSettings = GetTailwindSettings;
        var settings = getSettings is null
            ? null
            : ThreadHelper.JoinableTaskFactory.Run(getSettings);
        var current = Volatile.Read(ref _regexConfiguration);
        if (settings == current.Settings)
        {
            return current;
        }

        var updated = CreateRegexConfiguration(settings);
        return Interlocked.CompareExchange(ref _regexConfiguration, updated, current) == current
            ? updated
            : Volatile.Read(ref _regexConfiguration);
    }

    private static RegexConfiguration CreateRegexConfiguration(TailwindSettings? settings)
    {
        var configuration = new RegexConfiguration(settings);
        var custom = settings?.CustomRegexes;
        if (custom is null)
        {
            return configuration;
        }
        try
        {
            if (custom.Razor is not null)
            {
                configuration.OverrideRazor = custom.Razor.Override;
                configuration.Razor = [];

                foreach (var value in custom.Razor.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled, TimeSpan.FromSeconds(1));

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError(
                            $"Invalid custom regex: {value}. The regex must contain a capture group named 'content'."
                        );
                        return new(settings);
                    }

                    configuration.Razor.Add(newRegex);
                }
            }

            if (custom.HTML is not null)
            {
                configuration.OverrideNormal = custom.HTML.Override;
                configuration.Normal = [];

                foreach (var value in custom.HTML.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled, TimeSpan.FromSeconds(1));

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError(
                            $"Invalid custom regex: {value}. The regex must contain a capture group named 'content'."
                        );
                        return new(settings);
                    }

                    configuration.Normal.Add(newRegex);
                }
            }

            if (custom.JavaScript is not null)
            {
                configuration.OverrideJavaScript = custom.JavaScript.Override;
                configuration.JavaScript = [];

                foreach (var value in custom.JavaScript.Values)
                {
                    var newRegex = new Regex(value, RegexOptions.Compiled, TimeSpan.FromSeconds(1));

                    if (newRegex.GetGroupNames().Contains("content") is false)
                    {
                        VS.MessageBox.ShowError(
                            $"Invalid custom regex: {value}. The regex must contain a capture group named 'content'."
                        );
                        return new(settings);
                    }

                    configuration.JavaScript.Add(newRegex);
                }
            }

            return configuration;
        }
        catch (Exception ex)
        {
            ex.Log("Tailwind CSS: Setting custom regex failed.");
            VS.MessageBox.ShowError(
                "Invalid custom regex. Please check the output window for more details."
            );
            return new(settings);
        }
    }

    /// <summary>
    /// Gets all class matches in a CSS context.
    /// </summary>
    /// <param name="text">The text to search for class matches.</param>
    /// <returns>An enumerable collection of match objects.</returns>
    public static IEnumerable<Match> GetClassesCss(string text)
    {
        return _cssRegex.Matches(CommentRemover.StripCssComments(text)).Cast<Match>();
    }

    /// <summary>
    /// Gets all class matches in a normal HTML context.
    /// Includes: class="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesNormal(string text)
    {
        var configuration = GetRegexConfiguration();

        IEnumerable<Match> matches;
        if (
            !configuration.OverrideNormal
            || configuration.Normal is null
            || configuration.Normal.Count == 0
        )
        {
            matches = (
                (configuration.Normal?.SelectMany(regex => GetCustomRegexMatches(regex, text)))
                ?? []
            ).Concat(_classRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = configuration.Normal.SelectMany(regex => GetCustomRegexMatches(regex, text));
        }

        return matches.SelectMany(
            (Func<Match, IEnumerable<Match>>)(
                match =>
                {
                    var classContent = GetClassTextGroup(match);

                    if (_normalQuotePairRegex.IsMatch(classContent.Value))
                    {
                        var lastQuoteMatchIndex = classContent.Index;

                        List<Match> pairs = [];

                        while (
                            _normalQuotePairRegex.Match(
                                (string)text,
                                lastQuoteMatchIndex,
                                Math.Max(
                                    0,
                                    classContent.Index + classContent.Length - lastQuoteMatchIndex
                                )
                            )
                                is Match quoteMatch
                            && quoteMatch.Success
                        )
                        {
                            lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                            pairs.Add(quoteMatch);
                        }

                        return pairs;
                    }

                    return [match];
                }
            )
        );
    }

    /// <summary>
    /// Gets all class matches in a razor context.
    /// Includes: class="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesRazor(string text)
    {
        var configuration = GetRegexConfiguration();

        IEnumerable<Match> matches;
        if (
            !configuration.OverrideRazor
            || configuration.Razor is null
            || configuration.Razor.Count == 0
        )
        {
            matches = (
                (configuration.Razor?.SelectMany(regex => GetCustomRegexMatches(regex, text))) ?? []
            ).Concat(_razorClassRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = configuration.Razor.SelectMany(regex => GetCustomRegexMatches(regex, text));
        }

        return matches.SelectMany(
            (Func<Match, IEnumerable<Match>>)(
                match =>
                {
                    var classContent = GetClassTextGroup(match);

                    var razorSyntaxLocations = _razorSplitClassRegex
                        .Matches(classContent.Value)
                        .Cast<Match>()
                        .Where(match => match.Value.Length > 0 && match.Value[0] == '@');

                    if (_razorQuotePairRegex.IsMatch(classContent.Value))
                    {
                        var lastQuoteMatchIndex = classContent.Index;

                        List<Match> pairs = [];

                        while (
                            _razorQuotePairRegex.Match(
                                (string)text,
                                lastQuoteMatchIndex,
                                Math.Max(
                                    0,
                                    classContent.Index + classContent.Length - lastQuoteMatchIndex
                                )
                            )
                                is Match quoteMatch
                            && quoteMatch.Success
                        )
                        {
                            lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;

                            var localIndex = quoteMatch.Index - GetClassTextGroup(match).Index;

                            // If this quote pair match is inside a razor block, we also want to ignore it
                            if (
                                !razorSyntaxLocations.Any(r =>
                                    r.Index <= localIndex && r.Index + r.Length >= localIndex
                                )
                            )
                            {
                                pairs.Add(quoteMatch);
                            }
                        }

                        return pairs;
                    }

                    return [match];
                }
            )
        );
    }

    /// <summary>
    /// Gets all class matches in a JS context, such as with React.
    /// Includes: className="...".
    /// </summary>
    public static IEnumerable<Match> GetClassesJavaScript(string text)
    {
        var configuration = GetRegexConfiguration();

        IEnumerable<Match> matches;
        if (
            !configuration.OverrideJavaScript
            || configuration.JavaScript is null
            || configuration.JavaScript.Count == 0
        )
        {
            matches = (
                (configuration.JavaScript?.SelectMany(regex => GetCustomRegexMatches(regex, text)))
                ?? []
            ).Concat(_javaScriptClassRegex.Matches(text).Cast<Match>());
        }
        else
        {
            matches = configuration.JavaScript.SelectMany(regex =>
                GetCustomRegexMatches(regex, text)
            );
        }

        return matches.SelectMany(
            (Func<Match, IEnumerable<Match>>)(
                match =>
                {
                    var classContent = GetClassTextGroup(match);

                    if (_normalQuotePairRegex.IsMatch(classContent.Value))
                    {
                        var lastQuoteMatchIndex = classContent.Index;

                        List<Match> pairs = [];

                        while (
                            _normalQuotePairRegex.Match(
                                (string)text,
                                lastQuoteMatchIndex,
                                Math.Max(
                                    0,
                                    classContent.Index + classContent.Length - lastQuoteMatchIndex
                                )
                            )
                                is Match quoteMatch
                            && quoteMatch.Success
                        )
                        {
                            lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                            pairs.Add(quoteMatch);
                        }

                        return pairs;
                    }

                    return [match];
                }
            )
        );
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
        var configuration = GetRegexConfiguration();

        foreach (var customRegex in configuration.Razor ?? [])
        {
            var lastMatchIndex = 0;
            while (
                GetCustomRegexMatch(customRegex, text, lastMatchIndex) is Match match
                && match.Success
            )
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_razorQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (
                        _razorQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }
                yield return match;
            }
        }

        if (
            !configuration.OverrideRazor
            || configuration.Razor is null
            || configuration.Razor.Count == 0
        )
        {
            var lastMatchIndex = 0;

            while (_razorClassRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                var razorSyntaxLocations = _razorSplitClassRegex
                    .Matches(classText)
                    .Cast<Match>()
                    .Where(match => match.Value.Length > 0 && match.Value[0] == '@');

                if (_razorQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (
                        _razorQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;

                        var localIndex = quoteMatch.Index - GetClassTextGroup(match).Index;

                        // If this quote pair match is inside a razor block, we also want to ignore it
                        if (
                            !razorSyntaxLocations.Any(r =>
                                r.Index <= localIndex && r.Index + r.Length >= localIndex
                            )
                        )
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
        var configuration = GetRegexConfiguration();

        if (
            !configuration.OverrideNormal
            || configuration.Normal is null
            || configuration.Normal.Count == 0
        )
        {
            var lastMatchIndex = 0;

            while (_classRegex.Match(text, lastMatchIndex) is Match match && match.Success)
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (
                        _normalQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }

                yield return match;
            }
        }

        foreach (var customRegex in configuration.Normal ?? [])
        {
            var lastMatchIndex = 0;
            while (
                GetCustomRegexMatch(customRegex, text, lastMatchIndex) is Match match
                && match.Success
            )
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (
                        _normalQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
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
        var configuration = GetRegexConfiguration();

        if (
            !configuration.OverrideJavaScript
            || configuration.JavaScript is null
            || configuration.JavaScript.Count == 0
        )
        {
            var lastMatchIndex = 0;

            while (
                _javaScriptClassRegex.Match(text, lastMatchIndex) is Match match && match.Success
            )
            {
                lastMatchIndex = match.Index + match.Length;

                var classText = GetClassTextGroup(match).Value;

                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;

                    while (
                        _normalQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
                    {
                        lastQuoteMatchIndex = quoteMatch.Index + quoteMatch.Length;
                        yield return quoteMatch;
                    }
                    continue;
                }

                yield return match;
            }
        }

        foreach (var customRegex in configuration.JavaScript ?? [])
        {
            var lastMatchIndex = 0;
            while (
                GetCustomRegexMatch(customRegex, text, lastMatchIndex) is Match match
                && match.Success
            )
            {
                lastMatchIndex = match.Index + match.Length;
                var classText = GetClassTextGroup(match).Value;
                if (_normalQuotePairRegex.IsMatch(classText))
                {
                    var lastQuoteMatchIndex = match.Index;
                    while (
                        _normalQuotePairRegex.Match(
                            text,
                            lastQuoteMatchIndex,
                            Math.Min(text.Length - lastQuoteMatchIndex, match.Length)
                        )
                            is Match quoteMatch
                        && quoteMatch.Success
                    )
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
    /// from GetClasses___ (but not GetClassesRazor, since that should use SplitRazorClasses)
    /// </summary>
    /// <param name="text">An individual non-Razor class context (the ... in class="...")</param>
    public static IEnumerable<Match> SplitNonRazorClasses(string text)
    {
        var matches = _splitClassRegex.Matches(text).Cast<Match>();
        return matches;
    }

    private static IEnumerable<Match> GetCustomRegexMatches(Regex regex, string text)
    {
        try
        {
            return regex.Matches(text).Cast<Match>();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    private static Match? GetCustomRegexMatch(Regex regex, string text, int startAt)
    {
        try
        {
            return regex.Match(text, startAt);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    public static Group GetClassTextGroup(Match match)
    {
        // 'content' capture group matches the class value
        return match.Groups["content"];
    }
}
