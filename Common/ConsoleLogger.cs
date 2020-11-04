using System;

namespace Common.Core
{
    /// <summary>
    /// Simple console logger.
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        // 1: critical, 2: error, 3: warning, 4: info, 5: verbose
        private readonly int _maxLevel;

        public ConsoleLogger(int maxLevel)
        {
            this._maxLevel = maxLevel;
        }

        public void Error(string message)
        {
            if (this._maxLevel < 2) return;

            System.Console.WriteLine($"[ERROR] {GetMessage(message)}");
        }

        public void Warning(string message)
        {
            if (this._maxLevel < 3) return;

            System.Console.WriteLine($"[WARNING] {GetMessage(message)}");
        }

        public void Info(string message)
        {
            if (this._maxLevel < 4) return;

            System.Console.WriteLine(GetMessage(message));
        }

        private static string GetMessage(string message)
        {
            return $"{DateTime.Now:yyyy.mm.dd HH:mm:ss.fff}: {message}";
        }

        public void Verbose(string message)
        {
            if (this._maxLevel < 5) return;
            System.Console.WriteLine(GetMessage(message));
        }

        public void VerboseError(string message)
        {
            System.Console.WriteLine(GetMessage(message));
        }
    }
}
