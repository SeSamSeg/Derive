using System.Collections;
using Derive.Collections;
using Shouldly;

namespace Derive.Tests.Collections
{
    [Derive(typeof(DEnumerable<int>))]
    internal partial class TestEnumerable { }

    public class EnumerableTests
    {
        [Fact]
        public void Implements_IEnumerable()
        {
            (new TestEnumerable() is IEnumerable).ShouldBeTrue();
        }
    }
}
