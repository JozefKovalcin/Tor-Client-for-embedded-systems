using System.Runtime.CompilerServices;
using System.Text.Json;
using TorManager.Desktop.Presentation;

namespace TorManager.Desktop.Services;

internal interface IUiDispatcher
{
    void Post(Action update);

    Task ApplyAsync(Action update, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (token.IsCancellationRequested)
            {
                applied.TrySetCanceled(token);
                return;
            }
            try
            {
                update();
                applied.SetResult();
            }
            catch (Exception error) { applied.SetException(error); }
        });
        return applied.Task.WaitAsync(token);
    }
}

// UI seam only. W5 owns production contract bindings and task orchestration.
// A future adapter supplies already W1-validated, sanitized progress snapshots.
internal interface ITaskFeed
{
    IReadOnlyList<TaskCard> InitialTasks { get; }
    IReadOnlyList<DependencyCard> Dependencies { get; }
    IAsyncEnumerable<TaskCard> WatchAsync(CancellationToken cancellationToken);
}

internal sealed class SimulationTaskFeed : ITaskFeed
{
    public IReadOnlyList<TaskCard> InitialTasks { get; }
    public IReadOnlyList<DependencyCard> Dependencies { get; }

    public SimulationTaskFeed()
    {
        using var stream = typeof(SimulationTaskFeed).Assembly.GetManifestResourceStream("TorManager.Desktop.SampleData.Simulation.json")
            ?? throw new InvalidOperationException("Simulation resource absent");
        using var data = JsonDocument.Parse(stream);
        InitialTasks = data.RootElement.GetProperty("tasks").EnumerateArray().Select(TaskCard.FromJson).ToArray();
        Dependencies = data.RootElement.GetProperty("dependencies").EnumerateArray().Select(DependencyCard.FromJson).ToArray();
    }

    public async IAsyncEnumerable<TaskCard> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Finite, deterministic synthetic stream; no Tor/process/network operations.
        var task = InitialTasks.Single(row => row.TaskId == "demo-running");
        for (var step = 1; step <= 10; step++)
        {
            await Task.Delay(250, cancellationToken);
            yield return task with { Sequence = task.Sequence + step, Completed = step, Total = 10,
                Unit = "steps", Message = "SIMULATION: bounded task stream step " + step };
        }
        yield return task with { Sequence = task.Sequence + 11, State = "succeeded", Completed = 10,
            Total = 10, Unit = "steps", RawExitCode = 0, Message = "SIMULATION complete; no build or Tor operation executed" };
    }
}
