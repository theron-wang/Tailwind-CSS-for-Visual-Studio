using System.Collections.Generic;

namespace TailwindCSSIntellisense.Settings;

public class CustomRegexes
{
    public CustomRegex Razor { get; set; } = new();
    public CustomRegex HTML { get; set; } = new();
    public CustomRegex JavaScript { get; set; } = new();

    public class CustomRegex
    {
        public bool Override { get; set; } = false;
        public List<string> Values { get; set; } = [];
    }
}
