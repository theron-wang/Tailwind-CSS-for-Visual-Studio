using System.Collections.Generic;
using System.Threading.Tasks;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// Defines an interface for class operations that can be performed on a collection of class names.
/// </summary>
internal interface IClassOperation
{
    /// <summary>
    /// Performs an arbitrary operation on a collection of class names and returns the modified collection.
    /// </summary>
    /// <param name="classes">The collection of classes</param>
    /// <param name="filePath">The file path that the classes were from</param>
    /// <returns>The collection of classes, with the operation applied</returns>
    public Task<IEnumerable<string>> OperateAsync(IEnumerable<string> classes, string filePath);
}
