namespace Derive
{
    public static class DiagnosticProperties
    {
        public const string BaseTypeDocId = "BaseTypeDocId";
        public const string BaseTypeName = "BaseTypeName";

        // ';'-separated list of doc ids of the type arguments applied to the open base type
        // (empty for a non-generic base).
        public const string TypeArgDocIds = "TypeArgDocIds";

        // ';'-separated list of original-definition doc ids of the unimplemented members.
        public const string MemberDocIds = "MemberDocIds";
    }
}
