using Shouldly;

namespace Derive.Tests
{
    [Derive(typeof(Base))]
    public partial class PublicSub { }

    [Derive(typeof(Base))]
    internal partial class InternalSub { }

    [Derive<Base>]
    internal partial class GenericSyntaxSub { }

    internal class Base
    {
        public bool Expression() => true;

        public bool Body()
        {
            return true;
        }

        public bool Property => true;
    }

    internal abstract class AbstractBase
    {
        public abstract int Value { get; }

        public int Double() => Value * 2;
    }

    [Derive<AbstractBase>]
    internal partial class AbstractSub
    {
        public int Value => 21;
    }

    public partial class DeriverTests
    {
        [Derive(typeof(Base))]
        internal partial class PrivateSub { }

        [Derive(typeof(BaseUsingNamespace))]
        internal partial class UsingNamespace { }

        [Fact]
        public void Generic_attribute_syntax()
        {
            var sut = new GenericSyntaxSub();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Namespaced_on_public()
        {
            var sut = new PublicSub();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Namespaced_on_internal()
        {
            var sut = new InternalSub();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Private()
        {
            var sut = new PrivateSub();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Using_namespaces()
        {
            var sut = new UsingNamespace();
            sut.NonThrowingMember();
        }

        [Fact]
        public void Derives_property()
        {
            var sut = new PublicSub();
            sut.Property.ShouldBeTrue();
        }

        [Fact]
        public void Abstract_base()
        {
            var sut = new AbstractSub();
            sut.Double().ShouldBe(42);
        }
    }
}
