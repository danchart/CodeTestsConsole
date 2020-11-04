using System;

namespace Common.Core
{
    public class RealClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
