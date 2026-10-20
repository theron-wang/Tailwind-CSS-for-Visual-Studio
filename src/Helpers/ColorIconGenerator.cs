using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TailwindCSSIntellisense.Completions;

namespace TailwindCSSIntellisense.Helpers;

[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal class ColorIconGenerator
{
    [Import]
    internal ProjectConfigurationManager ProjectConfigurationManager { get; set; } = null!;

    private readonly ConditionalWeakTable<
        ProjectCompletionValues,
        ConcurrentDictionary<string, ImageSource>
    > _colorToRgbMapperCaches = new();

    internal void ClearCache(ProjectCompletionValues project)
    {
        if (_colorToRgbMapperCaches.TryGetValue(project, out var value))
        {
            value.Clear();
        }
    }

    internal ImageSource GetImageFromColor(
        ProjectCompletionValues projectCompletionValues,
        string stem,
        string color,
        int opacity = 100
    )
    {
        // Replaced configuration snapshots must not be kept alive by icon caches.
        var cache = _colorToRgbMapperCaches.GetOrCreateValue(projectCompletionValues);

        string value;
        var custom = false;
        if (
            projectCompletionValues.CustomColorMappers != null
            && projectCompletionValues.CustomColorMappers.Count > 0
        )
        {
            if (
                !projectCompletionValues.CustomColorMappers.TryGetValue(stem, out var dict)
                || (
                    dict.TryGetValue(color, out value)
                    && projectCompletionValues.ColorMapper.TryGetValue(color, out var value2)
                    && value == value2
                )
            )
            {
                if (cache.TryGetValue($"{color}/{opacity}", out var result))
                {
                    return result;
                }

                if (!projectCompletionValues.ColorMapper.TryGetValue(color, out value))
                {
                    return ProjectConfigurationManager.TailwindLogo;
                }
            }
            else
            {
                if (cache.TryGetValue($"{stem}/{color}/{opacity}", out var result))
                {
                    return result;
                }
                custom = true;
            }
        }
        else if (cache.TryGetValue($"{color}/{opacity}", out var result))
        {
            return result;
        }
        else if (!projectCompletionValues.ColorMapper.TryGetValue(color, out value))
        {
            return ProjectConfigurationManager.TailwindLogo;
        }

        if (
            string.IsNullOrWhiteSpace(value)
            || value.StartsWith("{noparse}")
            || value.StartsWith("var")
        )
        {
            return ProjectConfigurationManager.TailwindLogo;
        }

        byte r,
            g,
            b;

        if (ColorHelpers.ForceConvertToRgb(value) is { } converted && converted.Length == 3)
        {
            r = (byte)converted[0];
            g = (byte)converted[1];
            b = (byte)converted[2];
        }
        else
        {
            var rgb = value
                .Split(',')
                .Take(3)
                .Where(v => byte.TryParse(v, out _))
                .Select(byte.Parse)
                .ToArray();

            if (rgb.Length != 3)
            {
                // Something wrong happened: fall back to default tailwind icon
                return ProjectConfigurationManager.TailwindLogo;
            }

            r = rgb[0];
            g = rgb[1];
            b = rgb[2];
        }
        var a = (byte)Math.Round(opacity / 100d * 255);

        var pen = new Pen()
        {
            Thickness = 8,
            Brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
        };
        var mainImage = new GeometryDrawing()
        {
            Geometry = new RectangleGeometry(new Rect(4, 5, 9, 8)),
            Pen = pen,
        };

        // https://stackoverflow.com/questions/37663993/preventing-icon-color-and-size-distortions-when-bundling-a-visual-studio-project
        var pen2 = new Pen()
        {
            Thickness = 1,
            Brush = new SolidColorBrush(Color.FromArgb(1, 0, 255, 255)),
        };
        var vsPrevent = new GeometryDrawing()
        {
            Geometry = new RectangleGeometry(new Rect(16, 0, 1, 1)),
            Pen = pen2,
        };

        var geometry = new DrawingGroup();
        geometry.Children.Add(mainImage);
        geometry.Children.Add(vsPrevent);

        var output = new DrawingImage { Drawing = geometry };
        output.Freeze();

        var key = $"{color}/{opacity}";
        if (custom)
        {
            key = $"{stem}/{key}";
        }

        cache[key] = output;

        return output;
    }
}
