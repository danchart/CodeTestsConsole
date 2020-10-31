using System;

namespace CodeTestsConsole.Helpers
{
    public static class ArrayExtensions
    {
        public static void CopyToResize<T>(this T[] source, T[] dest)
            where T : unmanaged
        {
            if (source.Length < dest.Length)
            {
                Array.Resize(ref dest, source.Length);
            }

            Array.Copy(source, dest, source.Length);
        }
    }
}
