using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;

namespace TailwindCSSIntellisense.Adornments.Colors;

/// <summary>
/// Code adapted from https://github.com/madskristensen/EditorColorPreview/blob/master/src/Adornments/ColorAdornment.cs
/// </summary>
internal sealed class ColorAdornment : Border
{
    private static readonly SolidColorBrush _borderColor = (SolidColorBrush)
        Application.Current.Resources[VsBrushes.CaptionTextKey];
    private readonly ITextView _view;

    internal ColorAdornment(Color color, ITextView view)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _view = view;

        Padding = new Thickness(0);
        BorderThickness = new Thickness(1);
        BorderBrush = _borderColor;
        Margin = new Thickness(0, 0, 2, 3);
        Width = GetFontSize() + 2;
        Height = Width;
        Cursor = Cursors.Arrow;

        Update(color);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _view.Caret.MoveToNextCaretPosition();
        VS.Commands.ExecuteAsync("Edit.ListMembers").FireAndForget();
        e.Handled = true;
    }

    internal void Update(Color color)
    {
        Background = new SolidColorBrush(color);
    }

    private static int GetFontSize()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            IVsFontAndColorStorage storage = (IVsFontAndColorStorage)
                Package.GetGlobalService(typeof(IVsFontAndColorStorage));
            Guid guid = new("A27B4E24-A735-4d1d-B8E7-9716E1E3D8E0");
            if (
                storage != null
                && storage.OpenCategory(
                    ref guid,
                    (uint)(__FCSTORAGEFLAGS.FCSF_READONLY | __FCSTORAGEFLAGS.FCSF_LOADDEFAULTS)
                ) == VSConstants.S_OK
            )
            {
                LOGFONTW[] Fnt = [new()];
                FontInfo[] Info = [new()];
                storage.GetFont(Fnt, Info);
                return Info[0].wPointSize;
            }
        }
        catch { }

        return 0;
    }
}
