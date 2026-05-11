using System.Collections;
using Derive.Collections;
using Shouldly;

namespace Derive.Tests.Collections
{
    [Derive<DEnumerable<int>>]
    internal partial class TestEnumerable
    {
        private readonly IEnumerable<int> _items = Enumerable.Range(1, 5);

        public IEnumerator<int> GetEnumerator() => _items.GetEnumerator();
    }

    public class EnumerableTests
    {
        [Fact]
        public void Implements_IEnumerable_of_T()
        {
            IEnumerable<int> enumerable = new TestEnumerable();
            enumerable.ShouldBe([1, 2, 3, 4, 5]);
        }

        [Fact]
        public void Implements_IEnumerable()
        {
            IEnumerable enumerable = new TestEnumerable();
            enumerable.Cast<int>().ShouldBe([1, 2, 3, 4, 5]);
        }
    }
}
