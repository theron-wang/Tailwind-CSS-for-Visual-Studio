using System.Collections;

namespace TailwindCSSIntellisense.Tests.Helpers;

internal sealed class SinglePassEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
{
    public int EnumerationCount { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        if (++EnumerationCount != 1)
        {
            throw new InvalidOperationException("The input was enumerated more than once.");
        }
        return values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
