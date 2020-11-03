namespace Common.Core
{
    public sealed class AsyncCancelToken
    {
        private CancelStateEnum _state = CancelStateEnum.Waiting;

        private int _lockCount = 0;

        private readonly object _lock = new object();

        public bool IsCanceling => this._state == CancelStateEnum.Canceling;
        public bool IsCanceled => this._state == CancelStateEnum.Canceled;

        public void Cancel()
        {
            lock (_lock)
            {
                if (this._state == CancelStateEnum.Waiting)
                {
                    if (this._lockCount == 0)
                    {
                        // Resource released. Enter canceled state.
                        this._state = CancelStateEnum.Canceled;
                    }
                    else
                    {
                        // Resource in use, enter canceling state.
                        this._state = CancelStateEnum.Canceling;
                    }
                }
            }
        }

        public int Lock()
        {
            lock (_lock)
            {
                return ++this._lockCount;
            }
        }

        public int Unlock()
        {
            lock (_lock)
            {
                if (--this._lockCount == 0)
                {
                    if (this._state == CancelStateEnum.Canceling)
                    {
                        // Resource released. Enter canceled state.
                        this._state = CancelStateEnum.Canceled;
                    }
                }

                return this._lockCount;
            }
        }

        private enum CancelStateEnum
        { 
            Waiting,
            Canceling,
            Canceled,
        }
    }
}
