using Shouldly;

namespace Derive.Tests
{
    [Derive(typeof(Base))]
    public partial class PublicDerived { }

    [Derive(typeof(Base))]
    internal partial class InternalDerived { }

    [Derive<Base>]
    internal partial class GenericSyntaxDerived { }

    [Derive(typeof(GenericBase<>), TypeParams = ["int"])]
    internal partial class TestTypeParams { }

    internal class Base
    {
        public bool Expression() => true;

        public bool Body()
        {
            return true;
        }

        public bool Property => true;
    }

    internal class GenericBase<T>
    {
        public virtual T GetValue() => default!;
    }

    internal class VirtualBase
    {
        public virtual bool VirtualMethod() => true;

        public virtual bool VirtualMethod(int x) => true;

        public virtual bool VirtualProperty => true;
    }

    [Derive<VirtualBase>]
    internal partial class VirtualDerived
    {
        // same name as base virtuals but different argument — base copies should still happen
        public bool VirtualMethod(string x) => false;
    }

    [Derive<VirtualBase>]
    internal partial class VirtualOverriddenDerived
    {
        // overrides the no-arg overload — base copy should be suppressed
        public bool VirtualMethod() => false;

        // same name as a base virtual but different argument — base copy of VirtualMethod(int) should still happen
        public bool VirtualMethod(string x) => false;

        // overrides the property — base copy should be suppressed
        public bool VirtualProperty => false;
    }

    internal abstract class AbstractBase
    {
        public abstract int Value { get; }

        public int Double() => Value * 2;
    }

    [Derive<AbstractBase>]
    internal partial class AbstractDerived
    {
        public int Value => 21;
    }

    [Derive<AbstractBase>]
    internal abstract partial class DerivedAbstractBase
    {
        public int Value => 21;
        public abstract string Name { get; }
    }

    // Tests that the generator handles transitive [Derive]: applies both [Derive<DerivedAbstractBase>]
    // and its inherited [Derive<AbstractBase>], while also allowing an unrelated .NET base class (Exception).
    [Derive<DerivedAbstractBase>]
    internal partial class TransitiveDerived : Exception
    {
        public string Name => "test";
    }

    public partial class DeriverTests
    {
        [Derive(typeof(Base))]
        internal partial class PrivateDerived { }

        [Derive(typeof(BaseUsingNamespace))]
        internal partial class UsingNamespace { }

        [Fact]
        public void Generic_attribute_syntax()
        {
            var sut = new GenericSyntaxDerived();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Namespaced_on_public()
        {
            var sut = new PublicDerived();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Namespaced_on_internal()
        {
            var sut = new InternalDerived();
            sut.Expression().ShouldBeTrue();
            sut.Body().ShouldBeTrue();
        }

        [Fact]
        public void Private()
        {
            var sut = new PrivateDerived();
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
            var sut = new PublicDerived();
            sut.Property.ShouldBeTrue();
        }

        [Fact]
        public void Abstract_base()
        {
            var sut = new AbstractDerived();
            sut.Double().ShouldBe(42);
        }

        [Fact]
        public void Virtual_copies_when_not_implemented()
        {
            var sut = new VirtualDerived();
            sut.VirtualMethod().ShouldBeTrue();
            sut.VirtualMethod(0).ShouldBeTrue();
            sut.VirtualMethod("x").ShouldBeFalse();
            sut.VirtualProperty.ShouldBeTrue();
        }

        [Fact]
        public void Virtual_overridden_when_implemented()
        {
            var sut = new VirtualOverriddenDerived();
            sut.VirtualMethod().ShouldBeFalse();
            sut.VirtualMethod(0).ShouldBeTrue();
            sut.VirtualMethod("x").ShouldBeFalse();
            sut.VirtualProperty.ShouldBeFalse();
        }

        [Fact]
        public void Transitive_inheritance_pulls_all_members()
        {
            var sut = new TransitiveDerived();
            sut.Value.ShouldBe(21);
            sut.Name.ShouldBe("test");
            sut.Double().ShouldBe(42);
        }
    }
}
