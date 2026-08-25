using Planner.Chat;
using Planner.Core.Models;
using Planner.Core.Services;

namespace Planner.App.Services;

public sealed class OrgWorkService
{
    private readonly ServerChatClient _server;
    private readonly TaskService _tasks;
    private readonly LeaveService _leaves;
    private readonly UserAccountService _users;
    private readonly IReminderNotifier _toast;

    public OrgWorkService(
        ServerChatClient server,
        TaskService tasks,
        LeaveService leaves,
        UserAccountService users,
        IReminderNotifier toast)
    {
        _server = server;
        _tasks = tasks;
        _leaves = leaves;
        _users = users;
        _toast = toast;
        _server.WorkAssigned += dto => Dispatch(() => _ = AcceptAsync(dto, true));
        _server.LeaveUpdated += dto => Dispatch(() => _ = AcceptLeaveAsync(dto, true));
    }

    public async Task SyncInboxAsync()
    {
        if (!_users.UsesWork)
        {
            return;
        }

        try
        {
            foreach (var dto in await _server.ListWorkInboxAsync())
            {
                await AcceptAsync(dto, false);
            }

            var board = await _server.GetLeaveBoardAsync();
            foreach (var leave in board.Mine)
            {
                await AcceptLeaveAsync(leave, false);
            }
        }
        catch
        {
            // çevrimdışı
        }
    }

    private async Task AcceptAsync(WorkTaskDto dto, bool notify)
    {
        DateOnly.TryParse(dto.Date, out var date);
        if (date == default)
        {
            date = DateOnly.FromDateTime(DateTime.Today);
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(dto.Time) && TimeOnly.TryParse(dto.Time, out var parsed))
        {
            time = parsed;
        }

        Guid? by = null;
        if (Guid.TryParse(dto.AssignedByUserId, out var parsedBy))
        {
            by = parsedBy;
        }

        var notes = dto.Notes;
        if (!string.IsNullOrWhiteSpace(dto.AssignedByName))
        {
            notes = string.IsNullOrWhiteSpace(notes)
                ? "Atayan: " + dto.AssignedByName
                : notes + "\nAtayan: " + dto.AssignedByName;
        }

        await _tasks.UpsertWorkAssignmentAsync(dto.Id, dto.Title, notes, date, time, dto.AssignedByName, by);
        if (notify)
        {
            _toast.ShowInfo("Yeni iş görevi", dto.AssignedByName + " → " + dto.Title);
        }
    }

    private async Task AcceptLeaveAsync(OrgLeaveDto dto, bool notify)
    {
        var mine = string.Equals(dto.UserId, _server.UserId, StringComparison.OrdinalIgnoreCase);
        if (!mine)
        {
            if (notify)
            {
                var label = dto.Status == "pending" ? "İzin talebi" : "İzin güncellendi";
                _toast.ShowInfo(label, dto.UserName + " · " + dto.TypeName);
            }

            return;
        }

        var types = await _leaves.GetTypesAsync();
        var type = types.FirstOrDefault(t => t.Name.Equals(dto.TypeName, StringComparison.OrdinalIgnoreCase))
                   ?? types.FirstOrDefault(t => t.Id == LeaveIds.Annual)
                   ?? types.FirstOrDefault();
        if (type is null || !DateOnly.TryParse(dto.StartDate, out var start) || !DateOnly.TryParse(dto.EndDate, out var end))
        {
            return;
        }

        TimeOnly? startTime = TimeOnly.TryParse(dto.StartTime, out var st) ? st : null;
        TimeOnly? endTime = TimeOnly.TryParse(dto.EndTime, out var et) ? et : null;
        await _leaves.ApplyRemoteAsync(
            dto.Id,
            dto.ClientId,
            type.Id,
            (LeaveEntryKind)dto.EntryKind,
            (LeaveDurationKind)dto.DurationKind,
            start,
            end,
            startTime,
            endTime,
            dto.Note,
            LeaveService.MapServerStatus(dto.Status),
            dto.DurationMinutes);
        if (notify)
        {
            var title = dto.Status switch
            {
                "approved" => "İzin onaylandı",
                "rejected" => "İzin reddedildi",
                _ => "İzin talebi iletildi"
            };
            _toast.ShowInfo(title, dto.TypeName + (string.IsNullOrWhiteSpace(dto.DecidedByName) ? "" : " · " + dto.DecidedByName));
        }
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
