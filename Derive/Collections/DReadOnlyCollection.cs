using System;
using System.Collections;
using System.Collections.Generic;

namespace Derive.Collections
{
    /// <summary>
    /// Base class for deriving <see cref="IReadOnlyCollection{T}"/> implementations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <remarks>
    /// Apply <c>[Derive&lt;DReadOnlyCollection&lt;T&gt;&gt;]</c> to a <c>partial</c> class and implement
    /// <see cref="GetEnumerator"/> and <see cref="Count"/> — <see cref="ICollection{T}"/>,
    /// <see cref="ICollection"/>, and <see cref="IEnumerable.GetEnumerator"/> are provided automatically.
    /// <see cref="Contains"/> and <see cref="CopyTo"/> have loop-based defaults and can be overridden
    /// for better performance.
    /// </remarks>
    /// <example>
    /// <code>
    /// [Derive&lt;DReadOnlyCollection&lt;int&gt;&gt;]
    /// internal partial class EvenNumbers
    /// {
    ///     private readonly int[] _items;
    ///
    ///     public int Count => _items.Length;
    ///
    ///     public IEnumerator&lt;int&gt; GetEnumerator() => ((IEnumerable&lt;int&gt;)_items).GetEnumerator();
    /// }
    /// </code>
    /// </example>
    [Base]
    public abstract class DReadOnlyCollection<T>
        : DEnumerable<T>,
            IReadOnlyCollection<T>,
            ICollection<T>,
            ICollection
    {
        /// <inheritdoc/>
        public abstract int Count { get; }

        /// <inheritdoc cref="ICollection{T}.Contains"/>
        public virtual bool Contains(T item) => CollectionHelpers.ContainsWithLoop(this, item);

        /// <inheritdoc cref="ICollection{T}.CopyTo"/>
        public virtual void CopyTo(T[] array, int arrayIndex) =>
            CollectionHelpers.CopyToWithLoop(this, array, arrayIndex);

        bool ICollection<T>.IsReadOnly => true;

        void ICollection<T>.Add(T item) => throw new NotSupportedException();

        void ICollection<T>.Clear() => throw new NotSupportedException();

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        void ICollection.CopyTo(Array array, int index) =>
            CollectionHelpers.CopyToWithLoop(this, array, index);
    }
}
