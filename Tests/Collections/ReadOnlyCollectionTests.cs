using System.Collections;
using Derive.Collections;
using Shouldly;

namespace Derive.Tests.Collections
{
    [Derive<DReadOnlyCollection<int>>]
    internal partial class TestReadOnlyCollection
    {
        private readonly int[] _items = [1, 2, 3, 4, 5];

        public int Count => _items.Length;

        public override IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)_items).GetEnumerator();
    }

    public class ReadOnlyCollectionTests
    {
        [Fact]
        public void Implements_IEnumerable_of_T()
        {
            IEnumerable<int> collection = new TestReadOnlyCollection();
            collection.ShouldBe([1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Implements_IEnumerable()
        {
            IEnumerable collection = new TestReadOnlyCollection();
            collection.Cast<int>().ShouldBe([1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Implements_IReadOnlyCollection()
        {
            IReadOnlyCollection<int> collection = new TestReadOnlyCollection();
            collection.Count.ShouldBe(5);
        }

        [Fact]
        public void Implements_ICollection_of_T()
        {
            ICollection<int> collection = new TestReadOnlyCollection();
            collection.Count.ShouldBe(5);
            collection.IsReadOnly.ShouldBeTrue();
            collection.Contains(3).ShouldBeTrue();
            collection.Contains(9).ShouldBeFalse();

            var dest = new int[5];
            collection.CopyTo(dest, 0);
            dest.ShouldBe([1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Implements_ICollection()
        {
            ICollection collection = new TestReadOnlyCollection();
            collection.Count.ShouldBe(5);
            collection.IsSynchronized.ShouldBeFalse();

            var dest = new int[5];
            collection.CopyTo(dest, 0);
            dest.ShouldBe([1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Modifying_throws()
        {
            ICollection<int> collection = new TestReadOnlyCollection();
            Should.Throw<NotSupportedException>(() => collection.Add(6));
            Should.Throw<NotSupportedException>(() => collection.Clear());
            Should.Throw<NotSupportedException>(() => collection.Remove(1));
        }
    }
}
