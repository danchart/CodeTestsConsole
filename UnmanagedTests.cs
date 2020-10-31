using System.Runtime.InteropServices;

namespace CodeTestsConsole
{
    class UnmanagedTests
    {
        private static void Test()
        {
            //Test<CopyArrayData<int>>();

            var pool = new DataPool<Position>();
            pool.positions = new Position[256];


        }

        private static void TestIsManaged<T>()
            where T : unmanaged
        {
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DataPool<T>
            where T : unmanaged
        {
            public T[] positions;

        }

        internal struct Position
        {
            public float x, y;
        }
    }
}
