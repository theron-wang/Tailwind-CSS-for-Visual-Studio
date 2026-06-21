using System.Collections.Generic;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Configuration;

public class UnsetProjectCompletionValues : ProjectCompletionValues
{
    /// <summary>
    /// A list of default theme stems for use in css @theme completions.
    /// For example, this contains --color-, --animate-, etc.
    /// </summary>
    public List<string> ThemeStems { get; set; } = [];
}
