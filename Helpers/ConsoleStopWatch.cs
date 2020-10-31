using System;
using System.Diagnostics;

namespace CodeTestsConsole
{
    public class ConsoleStopWatch : IDisposable
    {
        Stopwatch _sw;
        string _before;

        public ConsoleStopWatch(string before = null)
        {
            _sw = new Stopwatch();
            _before = before;

            _sw.Start();
        }

        public void Dispose()
        {
            _sw.Stop();

            Console.WriteLine($"{_before}Completed in {_sw.Elapsed.TotalMilliseconds:N}ms.");
        }
    }
}
