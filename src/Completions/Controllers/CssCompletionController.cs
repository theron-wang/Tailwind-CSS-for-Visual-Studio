using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

namespace TailwindCSSIntellisense.Completions.Controllers;

#region Command Filter

[Export(typeof(IVsTextViewCreationListener))]
[ContentType("css")]
[ContentType("tcss")]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class CssCompletionController : IVsTextViewCreationListener
{
    [Import]
    internal IVsEditorAdaptersFactoryService AdaptersFactory { get; set; } = null!;

    [Import]
    internal ICompletionBroker CompletionBroker { get; set; } = null!;

    [Import]
    internal SVsServiceProvider ServiceProvider { get; set; } = null!;

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        var view = AdaptersFactory.GetWpfTextView(textViewAdapter)!;

        view.Properties.GetOrCreateSingletonProperty(() =>
            new CssCommandFilter(view, textViewAdapter, this)
        );
    }
}

internal sealed class CssCommandFilter : IOleCommandTarget
{
    private ICompletionSession? _currentSession;
    private readonly IOleCommandTarget _next;
    private readonly ICompletionBroker _broker;
    private readonly IWpfTextView _textView;
    private readonly CssCompletionController _provider;

    public CssCommandFilter(
        IWpfTextView textView,
        IVsTextView textViewAdapter,
        CssCompletionController provider
    )
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

    public int Exec(
        ref Guid pguidCmdGroup,
        uint nCmdID,
        uint nCmdexecopt,
        IntPtr pvaIn,
        IntPtr pvaOut
    )
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (VsShellUtilities.IsInAutomationFunction(_provider.ServiceProvider))
        {
            return _next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

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

        // Is the caret in an @apply context?
        bool isInApply = IsUsingAtDirective();
        string? classText = null;
        if (isInApply)
        {
            // Text after @apply to caret
            classText = GetClassText();
        }

        bool handled = false;
        bool retrigger = false;
        int hresult = VSConstants.S_OK;

        // 1. Pre-process
        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    handled = StartSession(isInApply);
                    Filter();
                    break;
                case VSConstants.VSStd2KCmdID.RETURN:
                    handled = Complete(false);
                    break;
                case VSConstants.VSStd2KCmdID.TAB:
                    handled = Complete(true);
                    if (isInApply)
                    {
                        retrigger = RetriggerIntellisense(classText);
                    }
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
            StartSession(true);
        }

        if (ErrorHandler.Succeeded(hresult))
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.TYPECHAR:
                        var character = GetTypeChar(pvaIn);
                        if (
                            character == ' '
                            || character == ':'
                            || (isInApply && (_currentSession == null || character == '/'))
                        )
                        {
                            _currentSession?.Dismiss();
                            StartSession(true);
                            Filter();
                        }
                        else if (_currentSession != null)
                        {
                            DismissOtherSessions();
                            Filter();
                        }
                        break;
                    case VSConstants.VSStd2KCmdID.DELETEWORDLEFT:
                        _currentSession?.Dismiss();
                        StartSession(true);
                        break;
                    case VSConstants.VSStd2KCmdID.BACKSPACE:
                        if (
                            isInApply
                            && classText is not null
                            && classText.Any()
                            && char.IsWhiteSpace(classText.Last())
                        )
                        {
                            break;
                        }
                        if (
                            isInApply
                            && (
                                _currentSession == null
                                || (
                                    classText is not null
                                    && (classText.EndsWith("/") || classText.EndsWith(":"))
                                )
                            )
                        )
                        {
                            _currentSession?.Dismiss();
                            StartSession(true);
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

    private bool RetriggerIntellisense(string? classText)
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
        {
            return;
        }

        _currentSession.SelectedCompletionSet.Filter();
        _currentSession.SelectedCompletionSet.SelectBestMatch();
    }

    /// <summary>
    /// Cancel the auto-complete session, and leave the text unmodified
    /// </summary>
    bool Cancel()
    {
        if (_currentSession == null)
        {
            return false;
        }

        _currentSession.Dismiss();

        return true;
    }

    /// <summary>
    /// Auto-complete text using the specified token
    /// </summary>
    bool Complete(bool force)
    {
        if (
            _currentSession == null
            || _currentSession.SelectedCompletionSet == null
            || _currentSession.SelectedCompletionSet.SelectionStatus == null
        )
        {
            return false;
        }

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

        var completionText = _currentSession
            .SelectedCompletionSet
            .SelectionStatus
            .Completion
            .InsertionText;
        // ) is for theme()
        var moveOneBack = completionText.EndsWith("]") || completionText.EndsWith(")");
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
    bool StartSession(bool shouldStartNew = false)
    {
        if (_currentSession != null)
        {
            return false;
        }

        var caretPoint = _textView.Caret.Position.Point.GetPoint(
            textBuffer => !textBuffer.ContentType.IsOfType("projection"),
            PositionAffinity.Predecessor
        );

        if (!caretPoint.HasValue)
        {
            return false;
        }

        var caret = caretPoint.Value;

        ITextSnapshot snapshot = caret.Snapshot;

        var completionActive = _broker.IsCompletionActive(_textView);

        if (completionActive)
        {
            _currentSession = _broker.GetSessions(_textView)[0];
            _currentSession.Dismissed += (sender, args) => _currentSession = null;
        }
        else if (shouldStartNew)
        {
            DismissOtherSessions();
            _currentSession = _broker.CreateCompletionSession(
                _textView,
                snapshot.CreateTrackingPoint(caret, PointTrackingMode.Positive),
                true
            );
            _currentSession.Dismissed += (sender, args) => _currentSession = null;
            _currentSession.Start();

            // Sometimes, creating another session will have irrelevant completions, so we need to filter
            Filter();
        }
        else
        {
            return false;
        }

        return true;
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return _next.QueryStatus(pguidCmdGroup, cCmds, prgCmds, pCmdText);
    }

    private bool IsUsingAtDirective()
    {
        var startPos = new SnapshotPoint(_textView.TextSnapshot, 0);
        var caretPos = _textView.Caret.Position.BufferPosition;

        var searchSnapshot = new SnapshotSpan(startPos, caretPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfSemicolon = text.LastIndexOf(";");
        var lastIndexOfAt = text.LastIndexOf("@apply");

        return lastIndexOfAt != -1 && lastIndexOfAt > lastIndexOfSemicolon;
    }

    private string GetClassText()
    {
        var startPos = new SnapshotPoint(_textView.TextSnapshot, 0);
        var caretPos = _textView.Caret.Position.BufferPosition;

        var searchSnapshot = new SnapshotSpan(startPos, caretPos);
        var text = searchSnapshot.GetText();

        var lastIndexOfAt = text.LastIndexOf("@apply");

        return text.Substring(lastIndexOfAt + "@apply".Length).TrimStart();
    }
}

#endregion
