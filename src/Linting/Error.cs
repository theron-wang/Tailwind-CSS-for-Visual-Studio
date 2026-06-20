using Microsoft.VisualStudio.Text;

namespace TailwindCSSIntellisense.Linting;

internal class Error
{
    public ITrackingSpan Span { get; set; } = null!;
    public string ErrorMessage { get; set; } = "";
    public ErrorType ErrorType { get; set; }
    public Suggestion? Suggestion { get; set; }
}
