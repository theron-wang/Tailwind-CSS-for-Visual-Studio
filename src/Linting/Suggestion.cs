using System.Collections.Generic;

namespace TailwindCSSIntellisense.Linting;

internal class Suggestion
{
    public string Message { get; set; } = null!;
    public IEnumerable<SuggestionFix> SuggestedFix { get; set; } = [];
}
