using System;
using System.Collections.Generic;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// A token representing either plain text or a series of classes. If representing plain text, then the Value property contains a single string in its collection.
/// If representing a series of classes, then the Value property contains an IEnumerable of strings, each representing a class.
/// </summary>
internal class ClassOpToken
{
    public IEnumerable<string> Value { get; set; } = [];
    public ClassOpTokenType Type { get; set; }

    /// <summary>
    /// Provided for code brevity in the various XClassOperator classes for plain text tokens.
    /// </summary>
    /// <param name="value">The string value for the token</param>
    public static implicit operator ClassOpToken(string value)
    {
        return new ClassOpToken { Value = [value], Type = ClassOpTokenType.Text };
    }

    /// <summary>
    /// Creates a ClassOpToken of type Class from a whitespace-separated string of classes. The Value property will contain an IEnumerable of strings, each representing a class.
    /// </summary>
    /// <param name="classes">The whitespace-separated string of classes</param>
    /// <returns>The ClassOpToken representing the classes</returns>
    public static ClassOpToken QuickCreateFromWhiteSpaceSeparatedClases(string classes)
    {
        return new ClassOpToken
        {
            Value = classes.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries),
            Type = ClassOpTokenType.Class,
        };
    }
}
