using System.Collections;
using System.Collections.Generic;

namespace Derive.Collections
{
    [Base]
    public abstract class DEnumerable<T> : IEnumerable<T>
    {
        public abstract IEnumerator<T> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
