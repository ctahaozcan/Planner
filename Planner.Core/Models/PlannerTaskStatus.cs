namespace Planner.Core.Models;

public enum PlannerTaskStatus
{
    Baslamadi = 0,
    DevamEdiyor = 1,
    Duraklatildi = 2,
    Tamamlandi = 3
}

public static class PlannerTaskStatusExtensions
{
    public static string ToDisplay(this PlannerTaskStatus status) => status switch
    {
        PlannerTaskStatus.Baslamadi => "Başlamadı",
        PlannerTaskStatus.DevamEdiyor => "Devam Ediyor",
        PlannerTaskStatus.Duraklatildi => "Duraklatıldı",
        PlannerTaskStatus.Tamamlandi => "Tamamlandı",
        _ => status.ToString()
    };
}
