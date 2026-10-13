using SilentPrintAgent.Core.Contracts;

namespace SilentPrintAgent.Services.Queue;

/// <summary>Wake-up hint only; SQLite remains the source of truth.</summary>
public sealed class InProcessPrintQueue : IPrintQueue, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    public Task SignalAsync(CancellationToken ct)
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
        return Task.CompletedTask;
    }
    public Task WaitAsync(CancellationToken ct) => _signal.WaitAsync(ct);
    public void Dispose() => _signal.Dispose();
}
