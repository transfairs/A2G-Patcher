// Windows 2000 / .NET 2.0 build only (A2G-Patch.csproj, Win2000=true): the part of .NET 4.0's tasks
// that PatchForm.ApplyChoice uses (Task.Factory.StartNew, ContinueWith on a scheduler,
// TaskScheduler.FromCurrentSynchronizationContext, Task.Exception), with the same semantics: the work
// runs on the thread pool, an exception ends up wrapped in Task.Exception, and the continuation runs
// once the work is done, through the given scheduler.

using System.Collections.Generic;

namespace System
{
    /// <summary>Wraps the exception of a failed task (.NET 4.0).</summary>
    public class AggregateException : Exception
    {
        /// <summary>Wraps <paramref name="innerException"/>.</summary>
        public AggregateException(Exception innerException)
            : base("One or more errors occurred.", innerException)
        {
        }
    }
}

namespace System.Threading.Tasks
{
    /// <summary>Work running on the thread pool.</summary>
    public class Task
    {
        private readonly object sync = new object();
        private readonly List<Action> continuations = new List<Action>();
        private bool completed;

        /// <summary>Creates tasks.</summary>
        public static TaskFactory Factory { get; } = new TaskFactory();

        /// <summary>The exception the work threw, wrapped; null if it ran through.</summary>
        public AggregateException Exception { get; private set; }

        /// <summary>True once the work has finished, successfully or not.</summary>
        public bool IsCompleted
        {
            get
            {
                lock (this.sync)
                    return this.completed;
            }
        }

        /// <summary>Runs <paramref name="continuation"/> through <paramref name="scheduler"/> after this task.</summary>
        public Task ContinueWith(Action<Task> continuation, TaskScheduler scheduler)
        {
            if (continuation == null)
                throw new ArgumentNullException(nameof(continuation));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            Task next = new Task();
            Action run = () => scheduler.Queue(() => next.Run(() => continuation(this)));
            bool done;
            lock (this.sync)
            {
                done = this.completed;
                if (!done)
                    this.continuations.Add(run);
            }
            if (done)
                run();
            return next;
        }

        /// <summary>Runs <paramref name="work"/>, keeps its exception, marks the task done and starts the continuations.</summary>
        internal void Run(Action work)
        {
            try
            {
                work();
            }
            catch (Exception error)
            {
                this.Exception = new AggregateException(error);
            }
            Action[] pending;
            lock (this.sync)
            {
                this.completed = true;
                pending = this.continuations.ToArray();
                this.continuations.Clear();
            }
            foreach (Action run in pending)
                run();
        }
    }

    /// <summary>Starts tasks.</summary>
    public class TaskFactory
    {
        /// <summary>Runs <paramref name="action"/> on the thread pool.</summary>
        public Task StartNew(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            Task task = new Task();
            ThreadPool.QueueUserWorkItem(_ => task.Run(action));
            return task;
        }
    }

    /// <summary>Decides where continuations run.</summary>
    public abstract class TaskScheduler
    {
        /// <summary>A scheduler that posts to the current thread's synchronization context (the window's thread).</summary>
        public static TaskScheduler FromCurrentSynchronizationContext()
        {
            SynchronizationContext context = SynchronizationContext.Current;
            if (context == null)
                throw new InvalidOperationException("The current SynchronizationContext may not be used as a TaskScheduler.");
            return new ContextScheduler(context);
        }

        /// <summary>Hands <paramref name="work"/> to the thread this scheduler runs on.</summary>
        internal abstract void Queue(Action work);

        private sealed class ContextScheduler : TaskScheduler
        {
            private readonly SynchronizationContext context;

            internal ContextScheduler(SynchronizationContext context)
            {
                this.context = context;
            }

            internal override void Queue(Action work) => this.context.Post(_ => work(), null);
        }
    }
}
