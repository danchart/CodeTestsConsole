using System;

namespace Common.Core
{
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
