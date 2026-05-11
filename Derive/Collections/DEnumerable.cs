using System.Collections;
using System.Collections.Generic;

namespace Derive.Collections
{
    /// <summary>
    /// Base class for deriving <see cref="IEnumerable{T}"/> implementations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <remarks>
    /// Apply <c>[Derive&lt;DEnumerable&lt;T&gt;&gt;]</c> to a <c>partial</c> class and implement
    /// <see cref="GetEnumerator"/> — the non-generic <see cref="IEnumerable.GetEnumerator"/> is
    /// generated automatically.
    /// </remarks>
    /// <example>
    /// <code>
    /// [Derive&lt;DEnumerable&lt;int&gt;&gt;]
    /// internal partial class EvenNumbers
    /// {
    ///     public IEnumerator&lt;int&gt; GetEnumerator() =>
    ///         Enumerable.Range(0, 10).Select(x => x * 2).GetEnumerator();
    /// }
    /// </code>
    /// </example>
    [Base]
    public abstract class DEnumerable<T> : IEnumerable<T>
    {
        /// <inheritdoc/>
        public abstract IEnumerator<T> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
