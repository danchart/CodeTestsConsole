namespace Common.Core
{
    public interface ILogger
    {
        void Info(string message);
        void Error(string message);
        void Warning(string message);
        void Verbose(string message);
        void VerboseError(string message);
    }
}