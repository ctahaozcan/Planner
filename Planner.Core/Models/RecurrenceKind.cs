namespace Planner.Core.Models;

public enum RecurrenceKind
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public enum OccurrenceMarkKind
{
    Skipped = 0,
    Completed = 1
}

public static class WeekdayBits
{
    public const int Monday = 1;
    public const int Tuesday = 2;
    public const int Wednesday = 4;
    public const int Thursday = 8;
    public const int Friday = 16;
    public const int Saturday = 32;
    public const int Sunday = 64;
    public const int Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday;
    public const int All = Weekdays | Saturday | Sunday;

    public static int For(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        _ => Sunday
    };

    public static bool Includes(int mask, DayOfWeek day) => mask != 0 && (mask & For(day)) != 0;

    public static string ToDisplay(int mask)
    {
        if (mask == All || mask == 0)
        {
            return "Her gün";
        }

        if (mask == Weekdays)
        {
            return "Hafta içi";
        }

        var names = new List<string>();
        if ((mask & Monday) != 0) names.Add("Pzt");
        if ((mask & Tuesday) != 0) names.Add("Sal");
        if ((mask & Wednesday) != 0) names.Add("Çar");
        if ((mask & Thursday) != 0) names.Add("Per");
        if ((mask & Friday) != 0) names.Add("Cum");
        if ((mask & Saturday) != 0) names.Add("Cmt");
        if ((mask & Sunday) != 0) names.Add("Paz");
        return string.Join(", ", names);
    }
}

public static class RecurrenceKindExtensions
{
    public static string ToDisplay(this RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Daily => "Her gün",
        RecurrenceKind.Weekly => "Haftalık",
        RecurrenceKind.Monthly => "Aylık",
        _ => "Yok"
    };
}
