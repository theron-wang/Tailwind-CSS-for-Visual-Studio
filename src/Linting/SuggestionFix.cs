using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Linting;

internal class SuggestionFix
{
    public string Replacement { get; set; } = null!;
    public ITrackingSpan ApplicableTo { get; set; } = null!;
}
