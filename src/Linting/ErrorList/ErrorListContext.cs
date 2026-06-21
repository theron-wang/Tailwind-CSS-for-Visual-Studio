using Community.VisualStudio.Toolkit;
using TailwindCSSIntellisense.Linting.Validators;

namespace TailwindCSSIntellisense.Linting.ErrorList;

internal class ErrorListContext
{
    public Validator Validator { get; set; } = null!;
    public TableDataSource TableDataSource { get; set; } = null!;
    public string File { get; set; } = null!;
    public Project Project { get; set; } = null!;
}
