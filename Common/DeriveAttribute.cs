namespace Derive
{
    /// <summary>
    /// Derive from base classes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DeriveAttribute : Attribute
    {
        public DeriveAttribute(Type baseType)
        {
            BaseType = baseType;
        }

        public Type BaseType { get; }
        public string[]? TypeParams { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class DeriveAttribute<T> : DeriveAttribute
    {
        public DeriveAttribute() : base(typeof(T)) { }
    }
}
