namespace CyberPipe.Contracts;

/// <summary>Сравнение коллекций по содержимому для DTO с равенством по значению.</summary>
internal static class CollectionEquality
{
    /// <summary>Сравнивает словари по парам ключ-значение без учёта порядка.</summary>
    /// <param name="left">Первый словарь.</param>
    /// <param name="right">Второй словарь.</param>
    /// <returns>Истина, если содержимое совпадает.</returns>
    public static bool DictionaryEquals(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out string? value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Сравнивает списки по элементам и порядку.</summary>
    /// <typeparam name="T">Тип элемента.</typeparam>
    /// <param name="left">Первый список.</param>
    /// <param name="right">Второй список.</param>
    /// <returns>Истина, если содержимое совпадает.</returns>
    public static bool ListEquals<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) =>
        left.SequenceEqual(right);

    /// <summary>Хэш словаря без учёта порядка пар.</summary>
    /// <param name="dictionary">Словарь.</param>
    /// <returns>Хэш содержимого.</returns>
    public static int DictionaryHash(IReadOnlyDictionary<string, string> dictionary)
    {
        int hash = 0;
        foreach (KeyValuePair<string, string> pair in dictionary)
        {
            hash ^= HashCode.Combine(pair.Key, pair.Value);
        }

        return hash;
    }

    /// <summary>Хэш списка с учётом порядка.</summary>
    /// <typeparam name="T">Тип элемента.</typeparam>
    /// <param name="list">Список.</param>
    /// <returns>Хэш содержимого.</returns>
    public static int ListHash<T>(IReadOnlyList<T> list)
    {
        var hash = new HashCode();
        foreach (T item in list)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
