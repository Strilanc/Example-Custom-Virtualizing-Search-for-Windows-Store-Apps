using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Runs actions one by one, dropping all but the last given action when choosing which action to run next.
/// Optionally has a cooldown period after each action is run.
/// </summary>
public sealed class NextActionThrottle {
    private static readonly Tuple<Func<Task>, TaskCompletionSource<bool>> RunningSygil = Tuple.Create<Func<Task>, TaskCompletionSource<bool>>(null, null);

    private Tuple<Func<Task>, TaskCompletionSource<bool>> _nextAction;

    /// <summary>Sets the action to be run immediately or when the currently running action has finished.</summary>
    /// <returns>A task for the action's task's completion or cancelation (if replaced).</returns>
    public Task SetNextTask(Func<Task> action) {
        if (action == null) throw new ArgumentNullException("action");
        var t = new TaskCompletionSource<bool>();
        var n = Interlocked.Exchange(ref _nextAction, Tuple.Create(action, t));
        if (n == null) {
            ProcessActionsAsync();
        } else if (!ReferenceEquals(n, RunningSygil)) {
            n.Item2.SetCanceled();
        }
        return t.Task;
    }
    private async void ProcessActionsAsync() {
        do {
            // get last set action
            var n = Interlocked.Exchange(ref _nextAction, RunningSygil);
            // re-enter run context, so it has the ability to run other stuff
            await Task.Yield();
            // run action
            await n.Item1();
            n.Item2.SetResult(true);
            // exit when no action set during execution or cooldown
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref _nextAction, null, RunningSygil), RunningSygil));
    }

    /// <summary>Sets the action to be run immediately or when the currently running action has finished.</summary>
    /// <returns>A task for the action's completion or cancelation (if replaced).</returns>
    public Task SetNextAction(Action action) {
        if (action == null) throw new ArgumentNullException("action");
        return SetNextTask(() => { action(); return Task.FromResult(true); });
    }
    /// <summary>Sets the action to be run immediately or when the currently running action has finished.</summary>
    /// <returns>A task for the function's result or cancelation (if replaced).</returns>
    public Task<T> SetNextFunc<T>(Func<T> func) {
        if (func == null) throw new ArgumentNullException("func");
        return SetNextTaskFunc(() => Task.FromResult(func()));
    }
    /// <summary>Sets the action to be run immediately or when the currently running action has finished.</summary>
    /// <returns>A task for the function's task's eventual result or cancelation (if replaced).</returns>
    public async Task<T> SetNextTaskFunc<T>(Func<Task<T>> func) {
        if (func == null) throw new ArgumentNullException("func");
        var result = default(T);
        await SetNextTask(async () => { result = await func(); });
        return result;
    }
}
