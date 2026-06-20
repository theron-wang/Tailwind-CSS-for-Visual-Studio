using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Adornments.Colors;

/// <summary>
/// Code adapted from https://github.com/madskristensen/EditorColorPreview/blob/master/src/Adornments/ColorAdornmentTagger.cs
/// </summary>
internal abstract class ColorTaggerBase : ITagger<IntraTextAdornmentTag>, IDisposable
{
    private readonly ITextBuffer _buffer;
    private readonly ITextView _view;
    private readonly ProjectConfigurationManager _projectConfigurationManager;
    private readonly CompletionConfiguration _completionConfiguration;
    protected readonly SettingsProvider _settingsProvider;

    private ProjectCompletionValues? _projectConfigurationValues;
    private bool _isProcessing;
    private General? _generalOptions;
    protected TailwindSettings? _settings;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
        Justification = "FileAndForget is ok"
    )]
    protected ColorTaggerBase(
        ITextBuffer buffer,
        ITextView view,
        ProjectConfigurationManager projectConfigurationManager,
        CompletionConfiguration completionConfiguration,
        SettingsProvider settingsProvider
    )
    {
        _buffer = buffer;
        _view = view;
        _projectConfigurationManager = projectConfigurationManager;
        _completionConfiguration = completionConfiguration;
        _settingsProvider = settingsProvider;

        _buffer.Changed += OnBufferChanged;
        General.Saved += GeneralSettingsChanged;
        _completionConfiguration.ConfigurationUpdated += ConfigurationUpdatedAsync;
        _settingsProvider.OnSettingsChanged += LocalSettingsChangedAsync;

        // Set _projectConfigurationValues without blocking
        ThreadHelper
            .JoinableTaskFactory.RunAsync(async () =>
            {
                _settings = await _settingsProvider.GetSettingsAsync();
                await ConfigurationUpdatedAsync();
            })
            .FileAndForget(nameof(TailwindCSSIntellisense) + "/ColorTagger/Initialize");
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        if (_isProcessing || e.Changes.Count == 0)
        {
            return;
        }

        try
        {
            _isProcessing = true;
            var start = e.Changes.First().NewSpan.Start;
            var end = e.Changes.Last().NewSpan.End;

            var startLine = e.After.GetLineFromPosition(start);
            var endLine = e.After.GetLineFromPosition(end);

            var span = new SnapshotSpan(e.After, Span.FromBounds(startLine.Start, endLine.End));
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span));
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private Task LocalSettingsChangedAsync(TailwindSettings settings)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public void Dispose()
    {
        _buffer.Changed -= OnBufferChanged;
        _completionConfiguration.ConfigurationUpdated -= ConfigurationUpdatedAsync;
        General.Saved -= GeneralSettingsChanged;
        _settingsProvider.OnSettingsChanged -= LocalSettingsChangedAsync;
    }

    /// <summary>
    /// Gets the class="" / @apply scopes in the specified span.
    /// </summary>
    protected abstract IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span);

    private void GeneralSettingsChanged(General general)
    {
        _generalOptions = general;
        TagsChanged?.Invoke(
            this,
            new SnapshotSpanEventArgs(
                new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length)
            )
        );
    }

    private async Task ConfigurationUpdatedAsync()
    {
        _projectConfigurationValues =
            await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
                _buffer.GetFileNameSafe()
            );
        TagsChanged?.Invoke(
            this,
            new SnapshotSpanEventArgs(
                new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length)
            )
        );
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    private bool Enabled()
    {
        _generalOptions ??= ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

        return _generalOptions.ShowColorPreviews
            && _generalOptions.UseTailwindCss
            && _settings is not null
            && _settings.ConfigurationFiles.Count > 0;
    }

    public IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetTags(
        NormalizedSnapshotSpanCollection spans
    )
    {
        var tags = new List<ITagSpan<IntraTextAdornmentTag>>();

        if (!spans.Any() || !Enabled())
        {
            return tags;
        }

        foreach (var span in spans)
        {
            tags.AddRange(GetAdornments(span));
        }

        return tags;
    }

    private IEnumerable<ITagSpan<IntraTextAdornmentTag>> GetAdornments(SnapshotSpan span)
    {
        foreach (var scope in GetScopes(span))
        {
            var color = GetRgbaFromClass(scope.GetText());
            if (color is null)
            {
                continue;
            }
            var winColor = Color.FromArgb(color[3], color[0], color[1], color[2]);
            var tag = new IntraTextAdornmentTag(
                new ColorAdornment(winColor, _view),
                null,
                PositionAffinity.Successor
            );

            yield return new TagSpan<IntraTextAdornmentTag>(
                new SnapshotSpan(scope.Snapshot, scope.Start, 0),
                tag
            );
        }
    }

    private byte[]? GetRgbaFromClass(string text)
    {
        if (_projectConfigurationValues is null)
        {
            return null;
        }

        // V4
        var v4Removed = false;
        if (string.IsNullOrWhiteSpace(_projectConfigurationValues.Prefix) == false)
        {
            if (text.StartsWith(_projectConfigurationValues.Prefix))
            {
                text = text.Substring(_projectConfigurationValues.Prefix!.Length);
                v4Removed = true;
            }
            else
            {
                return null;
            }
        }

        text = text.Split(':').Last();

        if (ImportantModifierHelper.IsImportantModifier(text))
        {
            text = text.Trim('!');
        }

        // V3
        if (!v4Removed && string.IsNullOrWhiteSpace(_projectConfigurationValues.Prefix) == false)
        {
            if (text.StartsWith(_projectConfigurationValues.Prefix))
            {
                text = text.Substring(_projectConfigurationValues.Prefix!.Length);
            }
            else if (text.StartsWith($"-{_projectConfigurationValues.Prefix}"))
            {
                text = text.Substring(_projectConfigurationValues.Prefix!.Length + 1);
            }
            else
            {
                return null;
            }
        }

        if (!_projectConfigurationValues.IsClassAllowed(text))
        {
            return null;
        }

        var endsWithArbitrary = text.LastIndexOf('[');
        var segmentText = text;

        if (endsWithArbitrary != -1)
        {
            segmentText = text.Substring(0, endsWithArbitrary);
        }
        else
        {
            endsWithArbitrary = text.LastIndexOf('(');

            if (endsWithArbitrary != -1)
            {
                segmentText = text.Substring(0, endsWithArbitrary);
            }
        }

        var segments = segmentText.Split(['-'], StringSplitOptions.RemoveEmptyEntries).ToList();

        if (endsWithArbitrary != -1)
        {
            segments.Add(text.Substring(endsWithArbitrary));
        }

        if (segments.Count >= 2)
        {
            string color;
            if (segments.Count >= 3)
            {
                color = string.Join("-", segments.Skip(segments.Count - 2));
            }
            else
            {
                color = segments[segments.Count - 1];
            }
            var stem = text.ReplaceLastOccurrence(color, "{0}");

            var opacityText = color.Split('/').Last();
            int opacity = 100;

            if (opacityText != color)
            {
                color = color.Replace($"/{opacityText}", "");
                if (int.TryParse(opacityText, out var o))
                {
                    opacity = o;
                }

                if (string.IsNullOrWhiteSpace(color))
                {
                    return null;
                }
            }

            if (
                (color[0] == '[' && color[color.Length - 1] == ']')
                || (color[0] == '(' && color[color.Length - 1] == ')')
            )
            {
                var c = color.Substring(1, color.Length - 2);

                if (color[0] == '(' && color[color.Length - 1] == ')')
                {
                    if (!_projectConfigurationValues.CssVariables.TryGetValue(c, out c))
                    {
                        return null;
                    }
                }

                if (ColorHelpers.IsHex(c, out var hex))
                {
                    var fromHex = System.Drawing.ColorTranslator.FromHtml($"#{hex}");
                    return [fromHex.R, fromHex.G, fromHex.B, fromHex.A];
                }
                else if (c.StartsWith("rgb"))
                {
                    var numbers = c.Substring(
                            c.IndexOf('(') + 1,
                            c.IndexOf(')') - c.IndexOf('(') - 1
                        )
                        .Split([' ', ',', '/'], StringSplitOptions.RemoveEmptyEntries);

                    var numbersAsBytes = numbers
                        .Take(3)
                        .Where(n => byte.TryParse(n, out _))
                        .Select(byte.Parse)
                        .ToArray();

                    if (numbersAsBytes.Length != 3)
                    {
                        return null;
                    }

                    if (numbers.Length == 3)
                    {
                        return [.. numbersAsBytes, 255];
                    }
                    else if (numbers.Length == 4)
                    {
                        // decimal or percent
                        if (
                            !double.TryParse(
                                numbers[3],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out var alpha
                            )
                        )
                        {
                            if (
                                numbers[3].EndsWith("%")
                                && double.TryParse(
                                    numbers[3].TrimEnd('%'),
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out alpha
                                )
                            )
                            {
                                alpha /= 100;
                            }
                            else
                            {
                                return [.. numbersAsBytes, 255];
                            }
                        }

                        return [.. numbersAsBytes, (byte)(alpha * 255)];
                    }
                }
                return null;
            }

            if (
                _projectConfigurationValues.DescriptionMapper.ContainsKey(
                    stem.Replace("{0}", "{c}")
                ) == false
                && _projectConfigurationValues.CustomDescriptionMapper.ContainsKey(
                    stem.Replace("{0}", "{c}")
                ) == false
            )
            {
                return null;
            }

            string value;
            if (
                _projectConfigurationValues.CustomColorMappers != null
                && _projectConfigurationValues.CustomColorMappers.ContainsKey(stem)
            )
            {
                if (
                    _projectConfigurationValues
                        .CustomColorMappers[stem]
                        .TryGetValue(color, out value) == false
                )
                {
                    return null;
                }
            }
            else if (_projectConfigurationValues.ColorMapper.TryGetValue(color, out value) == false)
            {
                return null;
            }

            if (ColorHelpers.ForceConvertToRgb(value) is int[] converted && converted.Length == 3)
            {
                return
                [
                    (byte)converted[0],
                    (byte)converted[1],
                    (byte)converted[2],
                    (byte)Math.Round(opacity / 100d * 255),
                ];
            }
            else
            {
                var rgb = value
                    .Split(',')
                    .Where(v => byte.TryParse(v, out _))
                    .Select(byte.Parse)
                    .ToArray();

                if (rgb.Length != 3)
                {
                    // inherit, current, etc.
                    return null;
                }

                return [.. rgb, (byte)Math.Round(opacity / 100d * 255)];
            }
        }
        return null;
    }
}
