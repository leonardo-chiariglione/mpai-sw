namespace AIF.Controller;

// The four states an AIM can be in, per the MPAI-AIF Basic API.
public enum AimState
{
    Idle,
    Running,
    Paused,
    Stopped
}

// Per-AIM lifecycle controller held by AimHost.
// Provides the CancellationToken (for Stop) and pause gate (for Pause/Resume)
// that an AIM's ProcessAsync uses to honour lifecycle signals from the Controller.
// The AIM never holds this directly — it is given a snapshot (AimContext) per call.
internal sealed class AimLifecycle : IDisposable
{
    private CancellationTokenSource _cts = new();
    private TaskCompletionSource    _pauseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AimState                _state = AimState.Idle;
    private readonly object         _lock = new();

    public AimState State
    {
        get { lock (_lock) return _state; }
    }

    // Called by AimHost.StartAimAsync — resets the lifecycle for a new run.
    public AimContext Start()
    {
        lock (_lock)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            _pauseGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseGate.TrySetResult();   // not paused — gate is open

            _state = AimState.Running;
        }

        return new AimContext(_cts.Token, _pauseGate.Task);
    }

    // Called by AimHost.StopAimAsync.
    public void Stop()
    {
        lock (_lock)
        {
            _state = AimState.Stopped;
            _cts.Cancel();
            _pauseGate.TrySetResult();   // unblock any pause wait
        }
    }

    // Called by AimHost.PauseAimAsync.
    public void Pause()
    {
        lock (_lock)
        {
            if (_state != AimState.Running) return;
            _state = AimState.Paused;
            // Replace gate with a new uncompleted TCS — AIM will block on it.
            _pauseGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    // Called by AimHost.ResumeAimAsync.
    public void Resume()
    {
        lock (_lock)
        {
            if (_state != AimState.Paused) return;
            _state = AimState.Running;
            _pauseGate.TrySetResult();   // open the gate
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}

// Snapshot of lifecycle signals given to an AIM for one ProcessAsync call.
// The AIM uses StopToken to detect a Stop request and awaits PauseGate to
// honour a Pause. Neither field exposes the AimLifecycle itself.
public readonly struct AimContext
{
    public static readonly AimContext None = new(CancellationToken.None, Task.CompletedTask);

    public CancellationToken StopToken { get; }
    public Task              PauseGate { get; }

    public AimContext(CancellationToken stopToken, Task pauseGate)
    {
        StopToken = stopToken;
        PauseGate = pauseGate;
    }

    // Convenience: await this to honour both Pause and Stop.
    // Call repeatedly at natural yield points inside ProcessAsync.
    public async Task CheckAsync()
    {
        StopToken.ThrowIfCancellationRequested();
        await PauseGate;
        StopToken.ThrowIfCancellationRequested();
    }
}
