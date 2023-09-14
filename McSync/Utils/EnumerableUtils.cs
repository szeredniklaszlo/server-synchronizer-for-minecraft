using System.Collections.Generic;
using System.Linq;

namespace McSync.Utils
{
    public class EnumerableUtils
    {
        public IDictionary<TKey, TValue> ToDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
        {
            return enumerable.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}