using System;
using System.Threading;

namespace ol;

public static class ThreadPool
{
    private static int _completed;
    private static int _count;
    private static Semaphore _semaphore = new Semaphore(0, int.MaxValue);
    private static SafeLinkedList<QueueItem> _queue = new();

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
        var head = _queue.Head;
        if (head == null)
        {
            return true;
        }

        var value = Interlocked.CompareExchange(ref _queue.Head, head.Next, head);

        if (value == head)
        {
            var queueItem = head.Data;
            queueItem.Function(queueItem.Data);
            Interlocked.Increment(ref _completed);
        }

        return false;
    }

    public static void QueueWork(Action<object> function, object data)
    {
        _queue.AddToHead(new QueueItem {Function = function, Data = data});
        Interlocked.Increment(ref _count);
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
        _count = 0;
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
