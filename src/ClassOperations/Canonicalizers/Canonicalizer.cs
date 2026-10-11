using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.ClassOperations.Operators;

namespace TailwindCSSIntellisense.ClassOperations.Canonicalizers;

/// <summary>
/// Canonicalizes a list of Tailwind CSS classes, ensuring they are in the correct format and order according to the Tailwind CSS framework.
/// </summary>
[Export(typeof(Canonicalizer))]
internal sealed class Canonicalizer : IClassOperation
{
    [Import]
    private Linting.Validators.Canonicalization.Canonicalizer _canonicalizer = null!;

    /// <summary>
    /// Canonicalizes the given list of classes for the specified file path, returning a new list of classes in canonical form.
    /// </summary>
    /// <param name="classes">The list of classes to canonicalize.</param>
    /// <param name="filePath">The path to the file containing the classes.</param>
    /// <returns>A new list of classes in canonical form.</returns>
    public async Task<IEnumerable<string>> OperateAsync(
        IEnumerable<string> classes,
        string filePath
    )
    {
        var classesAsList = new List<string>(classes);

        foreach (
            var canonicalization in await _canonicalizer.CanonicalizeAsync(classesAsList, filePath)
        )
        {
            var firstIndex = classesAsList.FindIndex(canonicalization.ClassNames.Contains);
            if (firstIndex < 0 || !canonicalization.ClassNames.All(classesAsList.Contains))
            {
                continue;
            }

            classesAsList.RemoveAll(canonicalization.ClassNames.Contains);
            classesAsList.Insert(firstIndex, canonicalization.CanonicalizedClassName);
        }

        return classesAsList;
    }
}
