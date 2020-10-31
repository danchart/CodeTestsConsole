namespace CodeTestsConsole
{
    public static class InterfaceTests
    {
        public static volatile int Result;
        public static volatile int Result2;

        public static volatile int A = 2, B = 3;

        public static void MethodCall()
        {
            Foo foo = new Foo();
            IFoo iFoo = new Foo();

            for (int i = 0; i < 3; i++)
            {
                using (new ConsoleStopWatch("Interface method call: "))
                {
                    for (int j = 0; j < 100000; j++)
                    {
                        Result = iFoo.Sum(A, B);
                    }
                }

                using (new ConsoleStopWatch("Direct method call: "))
                {
                    for (int j = 0; j < 100000; j++)
                    {
                        Result2 = foo.Sum(A, B);
                    }
                }
            }

            //System.Diagnostics.Debugger.Break();
        }

        internal interface IFoo
        {
            int Sum(int x, int y);

        }

        internal class Foo : IFoo
        {
            public int Sum(int x, int y)
            {
                return x + y;
            }
        }
    }
}
