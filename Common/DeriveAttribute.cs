using System;
using System.Collections.Immutable;

namespace Derive
{
    /// <summary>
    /// Derive from base classes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DeriveAttribute : Attribute
    {
        public DeriveAttribute(params Type[] baseTypes)
        {
            BaseTypes = [.. baseTypes];
        }

        public ImmutableArray<Type> BaseTypes { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DeriveAttribute<T> : DeriveAttribute
    {
        public DeriveAttribute() : base(typeof(T)) { }
    }
}
