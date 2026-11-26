namespace NEXTShot.Services;

public enum UpdateCheckIntervalKind
{
    None = 0,
    Weekly = 1,
    Monthly = 2
}

public static class UpdateCheckIntervalExtensions
{
    public static TimeSpan ToPeriod(this UpdateCheckIntervalKind kind) => kind switch
    {
        UpdateCheckIntervalKind.Weekly => TimeSpan.FromDays(7),
        UpdateCheckIntervalKind.Monthly => TimeSpan.FromDays(30),
        _ => TimeSpan.Zero
    };

    public static UpdateCheckIntervalKind ClampInterval(int value) =>
        Enum.IsDefined(typeof(UpdateCheckIntervalKind), value)
            ? (UpdateCheckIntervalKind)value
            : UpdateCheckIntervalKind.Weekly;
}