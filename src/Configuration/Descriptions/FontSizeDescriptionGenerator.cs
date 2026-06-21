using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.Json;

namespace TailwindCSSIntellisense.Configuration.Descriptions;

[Export(typeof(DescriptionGenerator))]
internal class FontSizeDescriptionGenerator : DescriptionGenerator
{
    public override string Handled { get; } = "fontSize";

    public override string? GetDescription(object value)
    {
        try
        {
            if (value is string)
            {
                return $"font-size: {value};";
            }
            else if (value is IEnumerable<string> array && array.Count() == 2)
            {
                var list = array.ToList();

                if (
                    list[1].Contains("lineHeight")
                    && list[1].Contains("letterSpacing")
                    && list[1].Contains("fontWeight")
                )
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(list[1]);

                    var fontSize = list[0];
                    var lineHeight = values!["lineHeight"];
                    var letterSpacing = values!["letterSpacing"];
                    var fontWeight = values!["fontWeight"];

                    return $"font-size: {fontSize};line-height: {lineHeight};letter-spacing: {letterSpacing};font-weight: {fontWeight};";
                }
                else
                {
                    var fontSize = list[0];
                    var lineHeight = list[1];

                    return $"font-size: {fontSize};line-height: {lineHeight};";
                }
            }
            else if (
                value is object[] tuple
                && tuple.Length == 2
                && tuple[0] is string fontSize
                && tuple[1] is Dictionary<string, string> dict
            )
            {
                var result = $"font-size: {fontSize};";

                if (dict.ContainsKey("lineHeight"))
                {
                    result += $"line-height: var(--tw-leading, {dict["lineHeight"]});";
                }
                if (dict.ContainsKey("letterSpacing"))
                {
                    result += $"letter-spacing: var(--tw-tracking, {dict["letterSpacing"]});";
                }
                if (dict.ContainsKey("fontWeight"))
                {
                    result += $"font-weight: var(--tw-font-weight, {dict["fontWeight"]});";
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            ex.Log();
        }
        return null;
    }
}
