using CodeTestsConsole.Helpers;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace CodeTestsConsole
{
    public static class ArrayTests
    {
        static readonly Random Random = new Random();

        public static void LoopOverElements()
        {
            // 1 million items
            var items = new Data[1000000];

            Console.WriteLine($"Length={items.Length:N0}");

            for (int i = 0; i < items.Length; i++)
            {
                items[i].isEnabled = Random.Next(0, 99) > 50;
            }

            int enabledCount = 0;

            using (new ConsoleStopWatch("InlineEnumerable: "))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    //if (items[i].isEnabled)
                    {
                        enabledCount++;
                    }
                }
            }
        }

        private struct Data
        {
            public bool isEnabled;

            public int x, y;
            public bool b;
            public string text;
        }
#if MOTHBALL
        public static void CopyArray()
        {
            bool b1 = TypeStuff.IsValueType<CopyArrayData<UnmanagedData>>();
            bool b2 = TypeStuff.IsValueType<CopyArrayDataNoInterface<UnmanagedData>>();
            bool b3 = TypeStuff.IsValueType<IBase>();
            bool b4 = TypeStuff.IsValueType<List<UnmanagedData>>();

            IBase[] sourceArray = new IBase[]
            {
                new CopyArrayData<UnmanagedData>
                {
                    Values = new UnmanagedData[]
                    {
                        new UnmanagedData { },
                        new UnmanagedData { },
                    }
                },
                new CopyArrayData<UnmanagedData>
                {
                    Values = new UnmanagedData[]
                    {
                        new UnmanagedData { },
                        new UnmanagedData { },
                    }
                },
                new CopyArrayData<UnmanagedData>
                {
                    Values = new UnmanagedData[]
                    {
                        new UnmanagedData { },
                        new UnmanagedData { },
                    }
                }
            };

            //var destArray = new IBase[sourceArray.Length];
            var destArray = new CopyArrayData<UnmanagedData>[sourceArray.Length];

            //Array.Copy(sourceArray, destArray, sourceArray.Length);

            for (int i = 0; i < sourceArray.Length; i++)
            {
                //pool.Values = new UnmanagedData[((CopyArrayData<UnmanagedData>)sourceArray[i]).Values.Length];

                //Array.Copy(((CopyArrayData<UnmanagedData>)sourceArray[i]).Values, destArray[i].Values, ((CopyArrayData<UnmanagedData>)sourceArray[i]).Values.Length);

                destArray[i] = new CopyArrayData<UnmanagedData>
                {
                    Values = default
                };

                ((CopyArrayData<UnmanagedData>)sourceArray[i]).CopyTo(ref destArray[i]);
            }

            ((CopyArrayData<UnmanagedData>)sourceArray[1]).Values[0].x = 13;

            Console.WriteLine($"Lengths={sourceArray.Length}, {destArray.Length}");
            Console.WriteLine("Source Data:");
            PrintValues(sourceArray);
            Console.WriteLine("Copied Data:");
            //PrintValues(destArray);
        }

        private static void PrintValues(IBase[] array)
        {
            Console.WriteLine($"\t{string.Join("\r\n\t", array.Select(x => string.Join("\t", ((CopyArrayData<UnmanagedData>)x).Values.Select(y => $"({y.x}, {y.y})"))))}");
        }

        internal interface IBase
        {
        }

        internal struct CopyArrayData<T> : IBase
            where T : unmanaged
        {
            public T[] Values;

            public void CopyTo(ref CopyArrayData<T> dest)
            {
                this.Values.CopyToResize(dest.Values);
            }
        }

        internal struct CopyArrayDataNoInterface<T>
            where T : unmanaged
        {
            public T[] Values;
        }



        private struct UnmanagedData
        {
            public int x, y;
        }

#endif
    }
}
