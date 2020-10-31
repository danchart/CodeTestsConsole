using System;

namespace CodeTestsConsole
{
    /// <summary>
    /// Simple console logger.
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        public void Error(string message)
        {
            System.Console.WriteLine($"[ERROR] {GetMessage(message)}");
        }

        public void Warning(string message)
        {
            System.Console.WriteLine($"[WARNING] {GetMessage(message)}");
        }

        public void Info(string message)
        {
            System.Console.WriteLine(GetMessage(message));
        }

        private static string GetMessage(string message)
        {
            return $"{DateTime.Now:yyyy.mm.dd HH:mm:ss.fff}: {message}";
        }

        public void Verbose(string message)
        {
            System.Console.WriteLine(GetMessage(message));
        }

        public void VerboseError(string message)
        {
            System.Console.WriteLine(GetMessage(message));
        }
    }
}
