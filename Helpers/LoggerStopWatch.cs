using Common.Core;
using System;
using System.Diagnostics;

namespace CodeTestsConsole
{
    public sealed class LoggerStopWatch : IDisposable
    {
        readonly ILogger _logger;
        readonly Stopwatch _sw;
        readonly string _before;

        public LoggerStopWatch(ILogger logger, string before = null)
        {
            _logger = logger;
            _sw = new Stopwatch();
            _before = before;

            _sw.Start();
        }

        public void Dispose()
        {
            _sw.Stop();

            _logger.Info($"{_before}Completed in {_sw.Elapsed.TotalMilliseconds:N}ms.");
        }
    }
}
