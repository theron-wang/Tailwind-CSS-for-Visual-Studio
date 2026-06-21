using System.Collections.Generic;

namespace TailwindCSSIntellisense.Helpers;

internal class DictionaryHelpers
{
    /// <summary>
    /// Merges dict2 into dict1. dict1 values take precedence.
    /// </summary>
    public static void MergeDictionaries<TKey, TValue>(
        Dictionary<TKey, TValue> dict1,
        Dictionary<TKey, TValue> dict2
    )
        where TKey : notnull
    {
        foreach (var kvp in dict2)
        {
            if (dict1.TryGetValue(kvp.Key, out var existingValue))
            {
                if (
                    existingValue is Dictionary<TKey, TValue> existingDict
                    && kvp.Value is Dictionary<TKey, TValue> newDict
                )
                {
                    MergeDictionaries(existingDict, newDict);
                }
            }
            else
            {
                dict1[kvp.Key] = kvp.Value;
            }
        }
    }
}
