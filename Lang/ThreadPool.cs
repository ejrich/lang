using System;
using System.Threading;

namespace Lang
{
    public static class ThreadPool
    {
        private static int Count;

        public static void Init()
        {
            for (var i = 0; i < Environment.ProcessorCount - 1; i++)
            {
                var workerThread = new Thread(ThreadWorker);
                workerThread.Start();
            }
        }

        public static void Stop()
        {
        }

        private static void ThreadWorker()
        {
            // TODO Check for work
            // while (true)
            // {
            // }
        }

        public static void QueueWork(Action<object> function, object data)
        {
        }

        private struct QueueItem
        {
            public Action<object> Function;
            public object Data;
        }
    }
}
