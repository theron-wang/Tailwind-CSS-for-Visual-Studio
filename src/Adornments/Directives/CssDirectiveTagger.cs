using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Options;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Adornments.Directives;

[Export(typeof(IViewTaggerProvider))]
[TagType(typeof(IntraTextAdornmentTag))]
[ContentType("css")]
[ContentType("tcss")]
[TextViewRole(PredefinedTextViewRoles.Document)]
[TextViewRole(PredefinedTextViewRoles.Analyzable)]
internal sealed class DirectiveCssTaggerProvider : IViewTaggerProvider
{
    [Import]
    internal DirectoryVersionFinder DirectoryVersionFinder { get; set; } = null!;

    [Import]
    internal ITextStructureNavigatorSelectorService TextStructureNavigatorSelector { get; set; } =
        null!;

    [Import]
    internal SettingsProvider SettingsProvider { get; set; } = null!;

    public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer)
        where T : ITag
    {
        return (
            buffer.Properties.GetOrCreateSingletonProperty(() =>
                new CssDirectiveTagger(
                    buffer,
                    TextStructureNavigatorSelector,
                    DirectoryVersionFinder,
                    SettingsProvider
                )
            ) as ITagger<T>
        )!;
    }

    /// <summary>
    /// Adds adornments to CSS directives, like @apply, to prevent confusion when squiggles are present.
    /// If/when VS adds support to intercept these warnings, remove this class.
    /// </summary>
    /// <remarks>See <a href="https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/105">https://github.com/theron-wang/Tailwind-CSS-for-Visual-Studio/issues/105</a></remarks>
    private class CssDirectiveTagger : ITagger<IntraTextAdornmentTag>, IDisposable
    {
        private readonly ITextBuffer _buffer;
        private readonly string _file;
        private readonly DirectoryVersionFinder _directoryVersionFinder;
        private readonly ITextStructureNavigator _textStructureNavigator;
        private readonly SettingsProvider _settingsProvider;

        private bool _isProcessing;
        private General? _generalOptions;
        private TailwindSettings? _tailwindSettings;

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "VSSDK007:ThreadHelper.JoinableTaskFactory.RunAsync",
            Justification = "FileAndForget is ok"
        )]
        internal CssDirectiveTagger(
            ITextBuffer buffer,
            ITextStructureNavigatorSelectorService textStructureNavigatorSelector,
            DirectoryVersionFinder directoryVersionFinder,
            SettingsProvider settingsProvider
        )
        {
            _buffer = buffer;
            _file = buffer.GetFileNameSafe();
            _directoryVersionFinder = directoryVersionFinder;
            _settingsProvider = settingsProvider;

            _textStructureNavigator = textStructureNavigatorSelector.GetTextStructureNavigator(
                buffer
            );

            _buffer.Changed += OnBufferChanged;
            General.Saved += GeneralSettingsChanged;
            _settingsProvider.OnSettingsChanged += SettingsChangedAsync;

            ThreadHelper
                .JoinableTaskFactory.RunAsync(async () =>
                {
                    await SettingsChangedAsync(await _settingsProvider.GetSettingsAsync());
                })
                .FileAndForget(
                    nameof(TailwindCSSIntellisense) + "/CssDirectiveTagger/InitializeSettings"
                );
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

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        public void Dispose()
        {
            _buffer.Changed -= OnBufferChanged;
            General.Saved -= GeneralSettingsChanged;
            _settingsProvider.OnSettingsChanged -= SettingsChangedAsync;
        }

        /// <summary>
        /// Gets relevant @ directives.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "VSTHRD102:Implement internal logic asynchronously",
            Justification = "Not expensive"
        )]
        protected IEnumerable<SnapshotSpan> GetScopes(SnapshotSpan span)
        {
            if (_tailwindSettings is null)
            {
                yield break;
            }

            int position = span.Start.Position;
            int end = span.End.Position;

            // Use the cache for _tailwindSettings here instead of fetching new since it _could_ be expensive
            var version = ThreadHelper.JoinableTaskFactory.Run(async () =>
                await _directoryVersionFinder.GetTailwindVersionAsync(_file, _tailwindSettings)
            );

            while (position < end)
            {
                var point = new SnapshotPoint(span.Snapshot, position);
                var extent = _textStructureNavigator.GetExtentOfWord(point);

                if (extent.IsSignificant)
                {
                    string text = extent.Span.GetText();
                    if (text == "@apply")
                    {
                        yield return extent.Span;
                    }
                    else if (
                        version == TailwindVersion.V3
                        && (text == "@tailwind" || text == "@config")
                    )
                    {
                        yield return extent.Span;
                    }
                    else if (
                        text == "@theme"
                        || text == "@source"
                        || text == "@utility"
                        || text == "@custom-variant"
                        || text == "@config"
                        || text == "@plugin"
                        || text == "@variant"
                        || text.StartsWith("@slot")
                    )
                    {
                        yield return extent.Span;
                    }

                    position = extent.Span.End.Position;
                }
                else
                {
                    position++;
                }
            }
        }

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

        private Task SettingsChangedAsync(TailwindSettings settings)
        {
            var first = _tailwindSettings is null;
            _tailwindSettings = settings;

            // Settings changed can happen a lot; only reload if it's the first time from null to non-null
            if (first)
            {
                TagsChanged?.Invoke(
                    this,
                    new SnapshotSpanEventArgs(
                        new SnapshotSpan(_buffer.CurrentSnapshot, 0, _buffer.CurrentSnapshot.Length)
                    )
                );
            }
            return Task.CompletedTask;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Usage",
            "VSTHRD102:Implement internal logic asynchronously",
            Justification = "Not expensive"
        )]
        private bool Enabled()
        {
            _generalOptions ??= ThreadHelper.JoinableTaskFactory.Run(General.GetLiveInstanceAsync);

            return _generalOptions.ShowColorPreviews && _generalOptions.UseTailwindCss;
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
                var tag = new IntraTextAdornmentTag(
                    new Image()
                    {
                        Source = ProjectConfigurationManager.TailwindLogo,
                        Margin = new Thickness(4, 0, 0, 0),
                    },
                    null,
                    PositionAffinity.Successor
                );

                yield return new TagSpan<IntraTextAdornmentTag>(
                    new SnapshotSpan(scope.Snapshot, scope.End, 0),
                    tag
                );
            }
        }
    }
}
