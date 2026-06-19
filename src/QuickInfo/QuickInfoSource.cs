using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.QuickInfo;

internal abstract class QuickInfoSource : IAsyncQuickInfoSource
{
    protected readonly ITextBuffer _textBuffer;
    protected readonly DescriptionGenerator _descriptionGenerator;
    private readonly ProjectConfigurationManager _projectConfigurationManager;
    private readonly SettingsProvider _settingsProvider;

    private const string PropertyKey = "tailwindintellisensequickinfoadded";

    public QuickInfoSource(
        ITextBuffer textBuffer,
        DescriptionGenerator descriptionGenerator,
        ProjectConfigurationManager projectConfigurationManager,
        SettingsProvider settingsProvider
    )
    {
        _textBuffer = textBuffer;
        _descriptionGenerator = descriptionGenerator;
        _projectConfigurationManager = projectConfigurationManager;
        _settingsProvider = settingsProvider;
    }

    public void Dispose() { }

    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken
    )
    {
        // session.Properties is to ensure that quick info is only added once (measure for #17)
        if (
            session.Content is null
            || session.Content.Any()
            || session.State == QuickInfoSessionState.Visible
            || session.State == QuickInfoSessionState.Dismissed
            || session.Properties.ContainsProperty(PropertyKey)
            || (await _settingsProvider.GetSettingsAsync()).ConfigurationFiles.Count == 0
        )
        {
            return null;
        }

        var triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);

        if (triggerPoint != null && IsInClassScope(session, out var classSpan) && classSpan != null)
        {
            var fullText = classSpan.Value.GetText();
            var unescapedFullText = UnescapeClass(fullText);

            var projectConfigurationValues =
                await _projectConfigurationManager.GetCompletionConfigurationByFilePathAsync(
                    _textBuffer.GetFileNameSafe()
                );

            if (!projectConfigurationValues.IsClassAllowed(unescapedFullText))
            {
                return null;
            }

            var desc = _descriptionGenerator.GetDescription(
                unescapedFullText,
                projectConfigurationValues
            );

            var span = _textBuffer.CurrentSnapshot.CreateTrackingSpan(
                classSpan.Value,
                SpanTrackingMode.EdgeInclusive
            );

            if (string.IsNullOrEmpty(desc) == false)
            {
                session.Properties.AddProperty(PropertyKey, true);

                var totalVariant = unescapedFullText.Contains(':')
                    ? _descriptionGenerator.GetTotalVariantDescription(
                        unescapedFullText.Substring(0, unescapedFullText.LastIndexOf(':')),
                        projectConfigurationValues
                    )
                    : [];

                ContainerElement descriptionFormatted;

                if (projectConfigurationValues.Version == TailwindVersion.V3)
                {
                    descriptionFormatted = DescriptionUIHelper.GetDescriptionAsUIFormatted(
                        fullText,
                        totalVariant.LastOrDefault(),
                        totalVariant.Length > 1
                            ? [.. totalVariant.Take(totalVariant.Length - 1)]
                            : [],
                        desc!
                    );
                }
                else
                {
                    descriptionFormatted = DescriptionUIHelper.GetDescriptionAsUIFormattedV4(
                        fullText,
                        totalVariant.FirstOrDefault(),
                        desc!
                    );
                }

                return new QuickInfoItem(span, descriptionFormatted);
            }
        }

        return null;
    }

    protected abstract bool IsInClassScope(IAsyncQuickInfoSession session, out SnapshotSpan? span);

    protected virtual string UnescapeClass(string input)
    {
        return input;
    }
}
