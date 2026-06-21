namespace TailwindCSSIntellisense.Helpers;

public class MutableKeyValuePair<TKey, TValue>(TKey key, TValue value)
{
    public TKey Key { get; set; } = key;
    public TValue Value { get; set; } = value;
}
