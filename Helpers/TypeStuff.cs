namespace CodeTestsConsole.Helpers
{
    public static class TypeStuff
    {
        public static bool IsValueType<T>()
        {
            return typeof(T).IsValueType;
        }
    }
}
