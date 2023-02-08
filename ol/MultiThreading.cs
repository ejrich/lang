using System;
using System.Threading;

namespace ol;

public static class ThreadPool
{
    public struct QueueItem
    {
        public Action<object> Function;
        public object Data;
    }

    public class WorkQueue
    {
        public int Completed;
        public int Processing;
        public int Count;
        public SafeLinkedList<QueueItem> Queue { get; } = new();
    }

    public static readonly WorkQueue ParseQueue = new();
    public static readonly WorkQueue TypeQueue = new();
    public static readonly WorkQueue IRQueue = new();

    private static readonly Semaphore _semaphore = new(0, int.MaxValue);

    public static void Init(bool noThreads)
    {
        if (!noThreads)
        {
            var threadCount = Environment.ProcessorCount - 1;
            for (var i = 0; i < threadCount; i++)
            {
                var workerThread = new Thread(ThreadWorker);
                workerThread.Start();
            }
        }
    }

    private static readonly WorkQueue[] Queues = { ParseQueue, TypeQueue, IRQueue };
    private static void ThreadWorker()
    {
        while (true)
        {
            var wait = true;
            foreach (var queue in Queues)
            {
                if (!ExecuteQueuedItem(queue))
                {
                    wait = false;
                    break;
                }
            }
            if (wait)
            {
                _semaphore.WaitOne();
            }
        }
    }

    private static bool ExecuteQueuedItem(WorkQueue queue)
    {
        var head = queue.Queue.Head;
        if (head == null)
        {
            return true;
        }

        var value = Interlocked.CompareExchange(ref queue.Queue.Head, head.Next, head);

        if (value == head)
        {
            Interlocked.Increment(ref queue.Processing);
            var queueItem = head.Data;
            queueItem.Function(queueItem.Data);
            Interlocked.Increment(ref queue.Completed);
        }

        return false;
    }

    public static void QueueWork(WorkQueue queue, Action<object> function, object data)
    {
        queue.Queue.AddToHead(new QueueItem {Function = function, Data = data});
        Interlocked.Increment(ref queue.Count);
        _semaphore.Release();
    }

    public static void CompleteWork(WorkQueue queue, bool determineByCompletions = true)
    {
        if (determineByCompletions)
        {
            while (queue.Completed < queue.Count)
            {
                ExecuteQueuedItem(queue);
            }

            queue.Completed = 0;
            queue.Processing = 0;
            queue.Count = 0;
        }
        else
        {
            while (queue.Processing < queue.Count)
            {
                ExecuteQueuedItem(queue);
            }
        }
    }
}

public class SafeLinkedList<T>
{
    public Node<T> Head;
    public Node<T> End;

    public void Add(T data)
    {
        var node = new Node<T> {Data = data};

        if (Head == null)
        {
            Head = node;
            End = node;
        }
        else
        {
            var originalEnd = ReplaceEnd(node);
            originalEnd.Next = node;
        }
    }

    public void AddToHead(T data)
    {
        var node = new Node<T> {Data = data, Next = Head};

        while (Interlocked.CompareExchange(ref Head, node, node.Next) != node.Next)
        {
            node.Next = Head;
        }
    }

    public Node<T> ReplaceEnd(Node<T> node)
    {
        var originalEnd = End;

        while (Interlocked.CompareExchange(ref End, node, originalEnd) != originalEnd)
        {
            originalEnd = End;
        }

        return originalEnd;
    }
}

public class Node<T>
{
    public T Data;
    public Node<T> Next;
}
