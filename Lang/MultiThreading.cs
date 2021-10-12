using System;
using System.Threading;

namespace Lang
{
    public static class ThreadPool
    {
        private static int _completed;
        private static int _next;
        private static int _count;
        // TODO Better way of handling this instead of max length semaphore
        private static Semaphore _semaphore = new Semaphore(0, int.MaxValue);

        private static QueueItem[] _queue = new QueueItem[256];

        public static void Init()
        {
            var threadCount = Environment.ProcessorCount - 1;
            for (var i = 0; i < threadCount; i++)
            {
                var workerThread = new Thread(ThreadWorker);
                workerThread.Start();
            }
        }

        private static void ThreadWorker()
        {
            while (true)
            {
                if (ExecuteQueuedItem())
                {
                    _semaphore.WaitOne();
                }
            }
        }

        private static bool ExecuteQueuedItem()
        {
            var next = _next;
            if (next >= _count)
            {
                return true;
            }

            var index = Interlocked.CompareExchange(ref _next, next + 1, next);

            if (index == next)
            {
                var queueItem = _queue[index];
                queueItem.Function(queueItem.Data);
                Interlocked.Increment(ref _completed);
            }

            return false;
        }

        public static void QueueWork(Action<object> function, object data)
        {
            var originalCount = _count;
            _queue[originalCount] = new QueueItem {Function = function, Data = data};

            Interlocked.CompareExchange(ref _count, originalCount + 1, originalCount);
            _semaphore.Release();
        }

        private struct QueueItem
        {
            public Action<object> Function;
            public object Data;
        }

        public static void CompleteWork()
        {
            while (_completed < _count)
            {
                ExecuteQueuedItem();
            }

            _completed = 0;
            _next = 0;
            _count = 0;
        }
    }

    public class LinkedList<T>
    {
        private class Node
        {
            public T Data;
            public Node Next;
        }

        private Node _head;
        private Node _end;

        public void Add(T data)
        {
            var node = new Node {Data = data};

            if (_head == null)
            {
                _head = node;
                _end = node;
            }
            else
            {
                Node originalEnd;
                do
                {
                    originalEnd = _end;
                }
                while (Interlocked.CompareExchange(ref _end, node, originalEnd) != originalEnd);

                originalEnd.Next = node;
            }
        }
    }
}
