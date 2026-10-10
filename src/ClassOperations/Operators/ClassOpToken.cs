using System;
using System.Collections.Generic;

namespace TailwindCSSIntellisense.ClassOperations.Operators;

/// <summary>
/// A token representing either plain text or a series of classes.
/// </summary>
internal class ClassOpToken
{
    public string Raw { get; set; } = null!;
    public ClassOpTokenType Type { get; set; }

    /// <summary>
    /// Gets the classes represented by this token. If the token is of type Text, an InvalidOperationException is thrown.
    /// </summary>
    /// <returns>The classes represented by this token.</returns>
    public virtual IEnumerable<string> GetClasses()
    {
        if (Type != ClassOpTokenType.Class)
        {
            throw new InvalidOperationException("Cannot get classes from a non-class token.");
        }

        return Raw.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Provided for code brevity in the various XClassOperator classes for plain text tokens.
    /// </summary>
    /// <param name="value">The string value for the token</param>
    public static implicit operator ClassOpToken(string value)
    {
        return new ClassOpToken { Raw = value, Type = ClassOpTokenType.Text };
    }
}

/// <summary>
/// A token representing a series of classes that have been split into individual class strings.
/// </summary>
internal class CustomSplitClassOpToken : ClassOpToken
{
    private readonly IEnumerable<string> _classes;

    /// <summary>
    /// Initializes a new instance of the CustomSplitClassOpToken class with the specified classes and raw string.
    /// </summary>
    /// <param name="classes">The classes</param>
    /// <param name="raw">The raw class content that the classes were extracted from</param>
    public CustomSplitClassOpToken(IEnumerable<string> classes, string raw)
    {
        Raw = raw;
        Type = ClassOpTokenType.Class;
        _classes = classes;
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetClasses()
    {
        return _classes;
    }
}
