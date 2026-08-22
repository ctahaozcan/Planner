namespace Planner.Core.Services;

public enum FocusPhase
{
    Idle = 0,
    Focus = 1,
    Break = 2
}

public sealed class FocusTimerService
{
    private readonly ITaskChangeSignal _signal;

    public FocusTimerService(ITaskChangeSignal signal)
    {
        _signal = signal;
    }

    public FocusPhase Phase { get; private set; }
    public DateTime? EndsAt { get; private set; }
    public Guid? LinkedTaskId { get; private set; }
    public string? LinkedTaskTitle { get; private set; }
    public int LastFocusMinutes { get; private set; } = 25;
    public int LastBreakMinutes { get; private set; } = 5;

    public event Action? Changed;

    public TimeSpan Remaining
    {
        get
        {
            if (EndsAt is null)
            {
                return TimeSpan.Zero;
            }

            var left = EndsAt.Value - DateTime.Now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public bool IsRunning => Phase != FocusPhase.Idle && EndsAt is not null;

    public void StartFocus(int minutes, Guid? taskId, string? taskTitle)
    {
        LastFocusMinutes = Math.Clamp(minutes, 1, 180);
        LinkedTaskId = taskId;
        LinkedTaskTitle = taskTitle;
        Phase = FocusPhase.Focus;
        EndsAt = DateTime.Now.AddMinutes(LastFocusMinutes);
        Raise();
    }

    public void StartBreak(int minutes)
    {
        LastBreakMinutes = Math.Clamp(minutes, 1, 60);
        Phase = FocusPhase.Break;
        LinkedTaskId = null;
        LinkedTaskTitle = null;
        EndsAt = DateTime.Now.AddMinutes(LastBreakMinutes);
        Raise();
    }

    public void Stop()
    {
        Phase = FocusPhase.Idle;
        EndsAt = null;
        LinkedTaskId = null;
        LinkedTaskTitle = null;
        Raise();
    }

    public bool TryCompleteIfDue(DateTime now)
    {
        if (Phase == FocusPhase.Idle || EndsAt is null || EndsAt > now)
        {
            return false;
        }

        return true;
    }

    public void CompleteDue()
    {
        EndsAt = null;
        Phase = FocusPhase.Idle;
        Raise(reschedule: false);
    }

    private void Raise(bool reschedule = true)
    {
        Changed?.Invoke();
        if (reschedule)
        {
            _signal.NotifyChanged();
        }
    }
}
