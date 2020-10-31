using System;
using System.Collections;
using System.Collections.Generic;

namespace CodeTestsConsole
{
    public static class EnumerableTests
    {
        public static volatile int Magic = 0;

        private const int ElementCount = 1000000;
        private const int DataElementCount = 64;

        public static void TestEnumerableVsIEnumerable()
        {
            // Arrange data

            var items = new Data[ElementCount];

            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new Data(DataElementCount);
            }

            Console.WriteLine($"Length={items.Length:N0}");

            Magic = 0;

            using (new ConsoleStopWatch("ForLoop: "))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    // note: big difference if we use indexing instead of direct reference
                    var item = items[i];

                    Magic += item.numbers[0];
                    Magic += item.numbers[1];
                    Magic += item.numbers[2];
                }
            }

            var staticEnum = new StaticEnumerable<Data>(items);

            Magic = 0;
            
            using (new ConsoleStopWatch("ForEach StaticEnumerable: "))
            {
                foreach (var item in staticEnum)
                {
                    Magic += item.numbers[0];
                    Magic += item.numbers[1];
                    Magic += item.numbers[2];
                }
            }

            Magic = 0;

            using (new ConsoleStopWatch("ForEach SubStaticEnumerable: "))
            {
                var subEnum = staticEnum.GetSubEnumerable();

                foreach (var item in staticEnum.GetSubEnumerable())
                {
                    Magic += item.numbers[0];
                    Magic += item.numbers[1];
                    Magic += item.numbers[2];
                }
            }

            var interfaceEnum = new InterfaceEnumerable<Data>(items);

            using (new ConsoleStopWatch("ForEach InterfaceEnumerable: "))
            {
                foreach (var item in interfaceEnum)
                {
                    Magic += item.numbers[0];
                    Magic += item.numbers[1];
                    Magic += item.numbers[2];
                }
            }
        }

        private class StaticEnumerable<T>
            where T : struct
        {
            private readonly T[] _values;

            public StaticEnumerable(T[] values)
            {
                _values = values;
            }

            public SubStaticEnumerable<T> GetSubEnumerable()
            {
                return new SubStaticEnumerable<T>(_values);
            }

            public struct SubStaticEnumerable<T>
                where T : struct
            {
                private readonly T[] _values;

                public SubStaticEnumerable(T[] values)
                {
                    _values = values;
                }

                public Enumerator<T> GetEnumerator()
                {
                    return new Enumerator<T>(_values);
                }

            }

            public Enumerator<T> GetEnumerator()
            {
                return new Enumerator<T>(_values);
            }

            public struct Enumerator<T>
                where T : struct
            {
                T[] _values;
                private int _current;

                internal Enumerator(T[] values)
                {
                    _values = values;
                    _current = -1;
                }

                public T Current => _values[_current];


                public bool MoveNext()
                {
                    return ++_current < _values.Length;
                }
            }
        }

        private class InterfaceEnumerable<T> : IEnumerable<T>
            where T : struct
        {
            private readonly T[] _values;

            public InterfaceEnumerable(T[] values)
            {
                _values = values;
            }

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return new Enumerator<T>(_values);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator<T>(_values);
            }

            public struct Enumerator<T> : IEnumerator<T>
                where T : struct
            {
                T[] _values;
                private int _current;

                internal Enumerator(T[] values)
                {
                    _values = values;
                    _current = -1;
                }

                T IEnumerator<T>.Current => _values[_current];

                object IEnumerator.Current => _current;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    return ++_current < _values.Length;
                }

                public void Reset()
                {
                    _current = -1;
                }
            }
        }

        private struct Data
        {
            public int[] numbers;

            public Data(int size)
            {
                numbers = new int[size];
            }
        }
    }
}
