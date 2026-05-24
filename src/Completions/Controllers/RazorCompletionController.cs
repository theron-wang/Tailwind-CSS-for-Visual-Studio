using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using TailwindCSSIntellisense.Parsers;

namespace TailwindCSSIntellisense.Completions.Controllers;

#region Command Filter

[Export(typeof(IVsTextViewCreationListener))]
[ContentType("razor")]
[ContentType("LegacyRazorCSharp")]
[ContentType("LegacyRazor")]
[ContentType("LegacyRazorCoreCSharp")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class RazorCompletionController : IVsTextViewCreationListener
{
    [Import]
    internal IVsEditorAdaptersFactoryService AdaptersFactory { get; set; } = null!;

    [Import]
    internal ICompletionBroker CompletionBroker { get; set; } = null!;

    [Import]
    internal SVsServiceProvider ServiceProvider { get; set; } = null!;

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        IWpfTextView view = AdaptersFactory.GetWpfTextView(textViewAdapter)!;

        view.Properties.GetOrCreateSingletonProperty(() => new RazorCommandFilter(view, textViewAdapter, this));
    }
}

internal sealed class RazorCommandFilter : IOleCommandTarget
{
    private ICompletionSession? _currentSession;
    private readonly IOleCommandTarget _next;
    private readonly ICompletionBroker _broker;
    private readonly IWpfTextView _textView;
    private readonly RazorCompletionController _provider;

    public RazorCommandFilter(IWpfTextView textView, IVsTextView textViewAdapter, RazorCompletionController provider)
    {
        _currentSession = null;

        _textView = textView;
        _broker = provider.CompletionBroker;
        _provider = provider;

        textViewAdapter.AddCommandFilter(this, out _next);
    }

    private char GetTypeChar(IntPtr pvaIn)
    {
        return (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Justification = "No reported errors in production, so we'll ignore this for now")]
    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        if (VsShellUtilities.IsInAutomationFunction(_provider.ServiceProvider))
        {
            return _next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        ThreadHelper.ThrowIfNotOnUIThread();

        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                case VSConstants.VSStd2KCmdID.RETURN:
                case VSConstants.VSStd2KCmdID.TAB:
                case VSConstants.VSStd2KCmdID.CANCEL:
                case VSConstants.VSStd2KCmdID.TYPECHAR:
                case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                case VSConstants.VSStd2KCmdID.BACKSPACE:
                    break;
                default:
                    return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
        }
        else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
        {
            switch ((VSConstants.VSStd97CmdID)nCmdID)
            {
                case VSConstants.VSStd97CmdID.Paste:
                case VSConstants.VSStd97CmdID.Undo:
                    break;
                default:
                    return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }
        }

        // Is the caret in a class="" scope?
        if (RazorParser.IsCursorInClassScope(_textView, out var classSpan) == false || classSpan is null)
        {
            return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        var truncatedClassSpan = new SnapshotSpan(classSpan.Value.Start, _textView.Caret.Position.BufferPosition);
        var classText = truncatedClassSpan.GetText();

        var last = classText.Split(' ').Last();

        bool handled = false;

        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            if (nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
            {
                var character = GetTypeChar(pvaIn);

                var newText = last + character;

                if (newText.StartsWith("@") && !(newText.StartsWith("@@") || newText.StartsWith("@(\"@\")")))
                {
                    _currentSession?.Dismiss();
                    return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }

                handled = true;
            }
            else if (nCmdID == (uint)VSConstants.VSStd2KCmdID.BACKSPACE &&
                !string.IsNullOrEmpty(last) && last.Length >= 1)
            {
                var newText = last.Substring(0, last.Length - 1);

                if (newText.StartsWith("@") && !(newText.StartsWith("@@") || newText.StartsWith("@(\"@\")")))
                {
                    _currentSession?.Dismiss();
                    return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }

                handled = true;
            }
        }

        if (!handled && last.StartsWith("@") && !(last.StartsWith("@@") || last.StartsWith("@(\"@\")")))
        {
            return _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        handled = false;
        bool retrigger = false;
        int hresult = VSConstants.S_OK;

        // 1. Pre-process
        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    handled = StartSession();
                    Filter();
                    break;
                case VSConstants.VSStd2KCmdID.RETURN:
                    handled = Complete(false);
                    break;
                case VSConstants.VSStd2KCmdID.TAB:
                    handled = Complete(true);
                    retrigger = RetriggerIntellisense(classText);
                    break;
                case VSConstants.VSStd2KCmdID.CANCEL:
                    handled = Cancel();
                    break;
            }
        }
        else if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
        {
            switch ((VSConstants.VSStd97CmdID)nCmdID)
            {
                case VSConstants.VSStd97CmdID.Paste:
                case VSConstants.VSStd97CmdID.Undo:
                    Cancel();
                    break;
            }
        }

        if (!handled)
        {
            hresult = _next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        if (retrigger)
        {
            StartSession();
        }

        if (ErrorHandler.Succeeded(hresult))
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        var character = GetTypeChar(pvaIn);

                        var newText = last + character;

                        if (_currentSession == null || character == ' ' || character == '/' || character == ':' || (!string.IsNullOrWhiteSpace(classText) && character == '!') ||
                            newText.StartsWith("@@") || newText.StartsWith("@(\"@\")"))
                        {
                            _currentSession?.Dismiss();
                            StartSession();
                            Filter();
                        }
                        else if (_currentSession != null)
                        {
                            Filter();
                        }
                        break;
                    case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                        _currentSession?.Dismiss();
                        StartSession();
                        break;
                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                        if (classText.Any() && char.IsWhiteSpace(classText.Last()))
                        {
                            break;
                        }
                        if (_currentSession == null || classText.EndsWith("/") || classText.EndsWith(":") || classText.Trim() == "!")
                        {
                            _currentSession?.Dismiss();
                            StartSession();
                            Filter();
                        }
                        else if (_currentSession != null)
                        {
                            Filter();
                        }
                        break;
                }
            }
        }

        return hresult;
    }

    private bool RetriggerIntellisense(string classText)
    {
        return classText != null && classText.EndsWith(":");
    }

    /// <summary>
    /// Dismisses the other sessions so two completion windows do not show up at once
    /// </summary>
    private void DismissOtherSessions()
    {
        foreach (var session in _broker.GetSessions(_textView))
        {
            if (session != _currentSession)
            {
                session.Dismiss();
            }
        }
    }

    /// <summary>
    /// Narrow down the list of options as the user types input
    /// </summary>
    private void Filter()
    {
        if (_currentSession == null || _currentSession.SelectedCompletionSet == null)
            return;

        _currentSession.SelectedCompletionSet.Filter();
        _currentSession.SelectedCompletionSet.SelectBestMatch();
    }

    /// <summary>
    /// Cancel the auto-complete session, and leave the text unmodified
    /// </summary>
    bool Cancel()
    {
        if (_currentSession == null)
            return false;

        _currentSession.Dismiss();

        return true;
    }

    /// <summary>
    /// Auto-complete text using the specified token
    /// </summary>
    bool Complete(bool force)
    {
        if (_currentSession == null || _currentSession.SelectedCompletionSet == null || _currentSession.SelectedCompletionSet.SelectionStatus == null)
            return false;

        if (!_currentSession.SelectedCompletionSet.SelectionStatus.IsSelected)
        {
            if (force)
            {
                _currentSession.SelectedCompletionSet.SelectBestMatch();
            }
            else
            {
                _currentSession.Dismiss();
                return false;
            }
        }

        var completionText = _currentSession.SelectedCompletionSet.SelectionStatus.Completion?.InsertionText;

        if (string.IsNullOrWhiteSpace(completionText))
        {
            _currentSession?.Dismiss();
            return false;
        }

        var moveOneBack = completionText!.EndsWith("]");
        var moveTwoBack = completionText.EndsWith("]:");
        _currentSession.Commit();

        if (moveOneBack)
        {
            _textView.Caret.MoveTo(_textView.Caret.Position.BufferPosition - 1);
        }
        else if (moveTwoBack)
        {
            _textView.Caret.MoveTo(_textView.Caret.Position.BufferPosition - 2);
        }

        return true;
    }

    /// <summary>
    /// Display list of potential tokens
    /// </summary>
    bool StartSession()
    {
        if (_currentSession != null && _currentSession.IsDismissed == false)
            return false;

        var caretPoint =
            _textView.Caret.Position.Point.GetPoint(
            textBuffer => !textBuffer.ContentType.IsOfType("projection"), PositionAffinity.Predecessor);

        if (!caretPoint.HasValue)
        {
            return false;
        }

        var caret = caretPoint.Value;

        ITextSnapshot snapshot = caret.Snapshot;

        var completionActive = _broker.IsCompletionActive(_textView);

        if (completionActive)
        {
            _currentSession = _broker.GetSessions(_textView).FirstOrDefault(s => s.SelectedCompletionSet?.DisplayName?.Contains("Shim") == false);

            if (_currentSession is not null)
            {
                _currentSession.Dismissed += (sender, args) => _currentSession = null;

                if (_currentSession.SelectedCompletionSet is TailwindCssCompletionSet)
                {
                    // Not handled correctly; sometimes these sessions do not get dismissed so we should dismiss them here
                    // This can be easily reproduced when typing bg-green-50/ and then backspacing /
                    // We expect transparency options to disappear, but they are still there

                    _currentSession.Dismiss();
                    _currentSession = null;
                }
                else
                {
                    return true;
                }
            }
        }

        if (!completionActive || _currentSession is null)
        {
            DismissOtherSessions();
            _currentSession = _broker.CreateCompletionSession(_textView, snapshot.CreateTrackingPoint(caret, PointTrackingMode.Positive), true);
            _currentSession.Dismissed += (sender, args) => _currentSession = null;
            _currentSession.Start();
        }

        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread", Justification = "No reported errors in production, so we'll ignore this for now")]
    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        return _next.QueryStatus(pguidCmdGroup, cCmds, prgCmds, pCmdText);
    }
}

#endregion
