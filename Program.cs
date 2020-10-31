using System;
using System.Linq;

namespace CodeTestsConsole
{
    class Program
    {
        static readonly Action[] _actionTypes = new Action[]
            {
                ArrayTests.LoopOverElements,
                //ArrayTests.CopyArray,
                EnumerableTests.TestEnumerableVsIEnumerable,
                InterfaceTests.MethodCall,
                TcpTests.TcpClientServerSendReceive,
                HttpTests.HttpClientServerSendReceive,
            };

        static void Main(string[] args)
        {
            PresentActions();

            // Command "loop".

            while (true)
            {
                string input = Console.ReadLine();
                int number;

                if (!int.TryParse(input, out number) ||
                    number > _actionTypes.Length)
                {
                    Console.WriteLine("Invalid number, try again.");

                    continue;
                }

                _actionTypes.ToArray()[number - 1].Invoke();

                break;
            }

            Console.WriteLine("Complete.");

            Console.ReadKey();
        }

        private static void PresentActions()
        {
            int i = 1;

            Console.WriteLine("Welcome to Code Tests!");
            Console.WriteLine("Select Routine:");
            Console.WriteLine($"\t{string.Join("\r\n\t", _actionTypes.Select(x => $"{i++ + ") " + x.Method.Name}"))}");
        }
    }
}
