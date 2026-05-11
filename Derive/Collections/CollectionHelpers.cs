using System;
using System.Collections.Generic;
using System.Linq;

namespace Derive.Collections
{
    public static class CollectionHelpers
    {
        public static bool ContainsWithLoop<T>(IEnumerable<T> values, T item) =>
            values.Contains(item, null);

        public static void CopyToWithLoop<T>(IEnumerable<T> values, T[] array, int arrayIndex)
        {
            int i = arrayIndex;
            foreach (var item in values)
                array[i++] = item;
        }

        public static void CopyToWithLoop<T>(IEnumerable<T> values, Array array, int arrayIndex)
        {
            int i = arrayIndex;
            foreach (var item in values)
                array.SetValue(item, i++);
        }
    }
}
