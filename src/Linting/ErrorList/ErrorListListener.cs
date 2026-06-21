using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using TailwindCSSIntellisense.Completions;
using TailwindCSSIntellisense.Configuration;
using TailwindCSSIntellisense.Linting.Validators;
using TailwindCSSIntellisense.Linting.Validators.Diagnostics;

namespace TailwindCSSIntellisense.Linting.ErrorList;

internal abstract class ErrorListListener : ITextViewCreationListener, IDisposable
{
    protected abstract Validator GetValidator(ITextView view);

    [Import]
    protected LinterUtilities _linterUtilities = null!;

    [Import]
    protected ProjectConfigurationManager _projectConfigurationManager = null!;

    [Import]
    protected CompletionConfiguration _completionConfiguration = null!;

    [Import]
    protected DiagnosticsAggregator _diagnosticsAggregator = null!;

    private readonly Dictionary<ITextBuffer, ErrorListContext> _contexts = [];

    public virtual void TextViewCreated(ITextView view)
    {
        if (_contexts.ContainsKey(view.TextBuffer) == false)
        {
            view.Closed += ViewClosed;

            var file = view.TextBuffer.GetFileName();

            if (file is null)
            {
                return;
            }

            var tableDataSource = new TableDataSource(Vsix.Name + file);

            var project = ThreadHelper
                .JoinableTaskFactory.Run(() => PhysicalFile.FromFileAsync(file))
                ?.ContainingProject;

            if (project is null)
            {
                return;
            }

            var validator = GetValidator(view);
            validator.BufferValidated += UpdateErrorList;

            _contexts[view.TextBuffer] = new()
            {
                File = file,
                Project = project,
                TableDataSource = tableDataSource,
                Validator = validator,
            };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "VSTHRD102:Implement internal logic asynchronously",
        Justification = "Not expensive"
    )]
    private void UpdateErrorList(ITextBuffer buffer)
    {
        if (_contexts.TryGetValue(buffer, out var context))
        {
            // Fix exception when a project is unloaded then reloaded
            try
            {
                _ = context.Project.Name;
            }
            catch (ObjectDisposedException)
            {
                var project = ThreadHelper
                    .JoinableTaskFactory.Run(() => PhysicalFile.FromFileAsync(context.File))
                    ?.ContainingProject;

                if (project is null)
                {
                    return;
                }

                context.Project = project;
            }

            var errorListItems = context
                .Validator.GetAllErrors()
                .Where(e => _linterUtilities.GetErrorSeverity(e.ErrorType) != ErrorSeverity.None)
                .Select(e =>
                {
                    var start = e.Span.GetStartPoint(buffer.CurrentSnapshot);

                    var line = buffer.CurrentSnapshot.GetLineFromPosition(start);
                    var severity = _linterUtilities.GetErrorSeverity(e.ErrorType);

                    return new ErrorListItem()
                    {
                        BuildTool = Vsix.Name,
                        Column = start.Position - line.Start.Position,
                        ErrorCategory = _linterUtilities.GetErrorTagFromSeverity(severity),
                        FileName = context.File,
                        Icon = GetIconByErrorType(severity),
                        Line = line.LineNumber,
                        Message = e.ErrorMessage,
                        ProjectName = context.Project.Name,
                        Severity = GetErrorCategory(severity),
                        ErrorCode = e.ErrorType.ToString(),
                    };
                });

            context.TableDataSource.CleanAllErrors();
            context.TableDataSource.AddErrors(errorListItems);
        }
    }

    private void ViewClosed(object sender, EventArgs e)
    {
        var view = (IWpfTextView)sender;
        view.Closed -= ViewClosed;

        if (_contexts.TryGetValue(view.TextBuffer, out var context))
        {
            if (context.Validator.BufferValidated is not null)
            {
                context.Validator.BufferValidated -= UpdateErrorList;
            }

            context.TableDataSource.CleanAllErrors();
        }
    }

    private ImageMoniker GetIconByErrorType(ErrorSeverity severity)
    {
        if (severity == ErrorSeverity.Warning)
        {
            return KnownMonikers.StatusWarning;
        }
        else if (severity == ErrorSeverity.Error)
        {
            return KnownMonikers.StatusError;
        }

        return KnownMonikers.StatusInformation;
    }

    private __VSERRORCATEGORY GetErrorCategory(ErrorSeverity severity)
    {
        if (severity == ErrorSeverity.Warning)
        {
            return __VSERRORCATEGORY.EC_WARNING;
        }
        else if (severity == ErrorSeverity.Error)
        {
            return __VSERRORCATEGORY.EC_ERROR;
        }

        return __VSERRORCATEGORY.EC_MESSAGE;
    }

    public void Dispose()
    {
        foreach (var context in _contexts.Values)
        {
#pragma warning disable CS8601 // Possible null reference assignment.
            if (context.Validator.BufferValidated is not null)
            {
                context.Validator.BufferValidated -= UpdateErrorList;
            }
#pragma warning restore CS8601 // Possible null reference assignment.

            context.TableDataSource.CleanAllErrors();
        }
    }
}
