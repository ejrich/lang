using System;
using System.Diagnostics;
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
        public int Count;
        public SafeLinkedList<QueueItem> Queue { get; } = new();
    }

    public static int RunThreadId;

    public static readonly WorkQueue ParseQueue = new();
    public static readonly WorkQueue IRQueue = new();

    private static readonly Semaphore Semaphore = new(0, int.MaxValue);
    private static readonly Semaphore RunMutex = new(0, 1);
    private static readonly Semaphore RunExecutingMutex = new(0, 1);

    public static void Init(bool noThreads)
    {
        if (!noThreads)
        {
            var threadCount = Environment.ProcessorCount - 2;
            for (var i = 0; i < threadCount; i++)
            {
                var workerThread = new Thread(ThreadWorker);
                workerThread.Start();
            }
        }

        var runDirectiveThread = new Thread(RunDirectiveWorker);
        runDirectiveThread.Start();
        RunThreadId = runDirectiveThread.ManagedThreadId;
    }

    private static bool _executingRun;
    private static FunctionIR _executingFunction;
    private static ScopeAst _executingScope;
    private static void RunDirectiveWorker()
    {
        while (true)
        {
            if (_executingFunction != null)
            {
                _executingRun = true;
                ProgramRunner.RunProgram(_executingFunction, _executingScope);
                _executingRun = false;
                _executingFunction = null;
                _executingScope = null;

                if (Messages.Intercepting)
                {
                    Messages.Intercepting = false;
                }
                else
                {
                    ReleaseRunExecuting();
                }
            }

            RunMutex.WaitOne();
        }
    }

    public static void ReleaseRunExecuting()
    {
        RunExecutingMutex.Release();
    }

    public static bool ExecuteRunDirective(FunctionIR function, ScopeAst scope)
    {
        if (_executingRun) return true;

        _executingFunction = function;
        _executingScope = scope;
        RunMutex.Release();
        RunExecutingMutex.WaitOne();

        return false;
    }

    private static readonly WorkQueue[] Queues = { ParseQueue, IRQueue };
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
                Semaphore.WaitOne();
            }
        }
    }

    private static bool ExecuteQueuedItem(WorkQueue queue)
    {
        var head = queue.Queue.RemoveHead();
        if (head == null)
        {
            return true;
        }

        var queueItem = head.Data;
        queueItem.Function(queueItem.Data);
        Interlocked.Increment(ref queue.Completed);

        return false;
    }

    public static void QueueWork(WorkQueue queue, Action<object> function, object data)
    {
        queue.Queue.Add(new QueueItem {Function = function, Data = data});
        Interlocked.Increment(ref queue.Count);
        Semaphore.Release();
    }

    public static void CompleteWork(WorkQueue queue)
    {
        while (queue.Completed < queue.Count)
        {
            ExecuteQueuedItem(queue);
        }

        queue.Completed = 0;
        queue.Count = 0;
    }
}

public class SafeLinkedList<T>
{
    public Node<T> Head;
    public Node<T> End;

    public void Add(T data)
    {
        var node = new Node<T> {Data = data};

        lock (this)
        {
            if (Head == null)
            {
                Head = node;
                End = node;
            }
            else
            {
                End.Next = node;
                End = node;
            }
        }
    }

    public Node<T> RemoveHead()
    {
        lock (this)
        {
            var head = Head;
            if (head == null) return null;

            var next = head.Next;
            Head = next;
            if (End == head)
            {
                End = next;
            }

            return head;
        }
    }
}

public class Node<T>
{
    public T Data;
    public Node<T> Next;
}
