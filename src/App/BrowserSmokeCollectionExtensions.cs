using System;
using System.Collections.Generic;

namespace DesktopBuddy.App;

/// <summary>Small allocation-free predicate helper used by the browser interaction smoke driver.</summary>
internal static class BrowserSmokeCollectionExtensions
{
    public static bool Exists<T>(this IReadOnlyList<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        for (int index = 0; index < source.Count; index++)
        {
            if (predicate(source[index]))
                return true;
        }

        return false;
    }
}
