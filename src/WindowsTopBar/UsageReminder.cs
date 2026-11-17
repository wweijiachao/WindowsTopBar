namespace WindowsTopBar;

[Flags]
internal enum UsageInterruption { None = 0, Locked = 1, DisplayOff = 2, Suspended = 4 }

internal enum UsagePhase { Hidden, Normal, FirstReminder, SecondReminder, Resting }

internal sealed class UsageReminder
{
    private const long RestInputGraceMilliseconds = 5000;
    private bool _enabled;
    private UsageInterruption _interruptions;
    private long _startedAt;
    private long? _resumeAfter;

    public bool IsAvailable => _enabled && _interruptions == UsageInterruption.None;
    public bool IsResting => _resumeAfter.HasValue;
    public bool CanStartRest => IsAvailable && !IsResting;

    public void SetEnabled(bool enabled, long awakeMilliseconds)
    {
        if (_enabled != enabled)
        {
            _enabled = enabled;
            _resumeAfter = null;
            _startedAt = awakeMilliseconds;
        }
    }

    public void SetInterrupted(UsageInterruption interruption, bool interrupted, long awakeMilliseconds, long uptimeMilliseconds)
    {
        var previous = _interruptions;
        _interruptions = interrupted ? previous | interruption : previous & ~interruption;

        // A resume or unlock can be the first notification after Windows interrupted the session.
        if (previous == _interruptions && (interrupted || interruption == UsageInterruption.DisplayOff))
        {
            return;
        }

        _startedAt = awakeMilliseconds;
        if (IsAvailable && _resumeAfter is long resumeAfter)
        {
            _resumeAfter = Math.Max(resumeAfter, uptimeMilliseconds + 1);
        }
    }

    public void StartRest(long uptimeMilliseconds)
    {
        if (CanStartRest)
        {
            _resumeAfter = uptimeMilliseconds + RestInputGraceMilliseconds;
        }
    }

    public bool ShouldCheckInput(long uptimeMilliseconds) =>
        IsAvailable && _resumeAfter is long resumeAfter && uptimeMilliseconds >= resumeAfter;

    public void ObserveInput(long lastInputMilliseconds, long awakeMilliseconds)
    {
        if (IsAvailable && _resumeAfter is long resumeAfter && lastInputMilliseconds >= resumeAfter)
        {
            _resumeAfter = null;
            _startedAt = awakeMilliseconds;
        }
    }

    public long ElapsedMinutes(long awakeMilliseconds) => Math.Max(0, awakeMilliseconds - _startedAt) / 60_000;

    public UsagePhase GetPhase(long awakeMilliseconds, int firstMinutes, int secondMinutes)
    {
        if (!IsAvailable)
        {
            return UsagePhase.Hidden;
        }
        if (_resumeAfter is not null)
        {
            return UsagePhase.Resting;
        }

        var elapsed = ElapsedMinutes(awakeMilliseconds);
        return elapsed >= secondMinutes ? UsagePhase.SecondReminder
            : elapsed >= firstMinutes ? UsagePhase.FirstReminder
            : UsagePhase.Normal;
    }
}
