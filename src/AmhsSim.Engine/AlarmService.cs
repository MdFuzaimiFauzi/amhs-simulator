using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using AmhsSim.Engine.Data;
using AmhsSim.Protocol;

namespace AmhsSim.Engine;

/// <summary>
/// Turns vehicle alarms, link losses and job aborts into AlarmRecord rows.
/// Producers (socket threads) only write to a Channel; one consumer does the DB work,
/// so a slow database can never stall a vehicle's read loop.
/// </summary>
public sealed class AlarmService : BackgroundService
{
    private sealed record AlarmChange(string Source, string Code, string Message, bool alarmOn);

    private readonly Channel<AlarmChange> _queue = Channel.CreateUnbounded<AlarmChange>();
    private readonly Dictionary<string, (AlarmCode Alarm, bool Online)> _last = [];
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AlarmService> _logger;

    public AlarmService(VehicleManager vehicles, IDbContextFactory<AppDbContext> dbFactory, ILogger<AlarmService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        vehicles.VehicleChanged += OnVehicleChanged;
    }

    public event Action? Changed;

    public void Raise(string source, string code, string message) =>
        _queue.Writer.TryWrite(new AlarmChange(source, code, message, alarmOn: true));

    public async Task ClearAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var alarm = await db.Alarms.FindAsync(id);
        if (alarm is null || alarm.ClearedAt is not null) return;
        alarm.ClearedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Changed?.Invoke();
    }

    private void OnVehicleChanged(VehicleConnection v)
    {
        var alarm = v.Status?.Alarm ?? AlarmCode.None;
        bool online = v.IsOnline;
        lock (_last)
        {
            var prev = _last.TryGetValue(v.Name, out var p) ? p : (Alarm: AlarmCode.None, Online: false);
            _last[v.Name] = (alarm, online);

            if (prev.Alarm != alarm)
            {
                if (prev.Alarm != AlarmCode.None)
                    _queue.Writer.TryWrite(new(v.Name, prev.Alarm.ToString(), "", alarmOn: false));
                if (alarm != AlarmCode.None)
                    _queue.Writer.TryWrite(new(v.Name, alarm.ToString(), $"Vehicle reported {alarm}", alarmOn: true));
            }

            if (prev.Online && !online)
                _queue.Writer.TryWrite(new(v.Name, "LinkDown", "Lost connection to vehicle", alarmOn: true));
            else if (!prev.Online && online)
                _queue.Writer.TryWrite(new(v.Name, "LinkDown", "", alarmOn: false));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var change in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await SaveAsync(change, ct);
                    Changed?.Invoke();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to save alarm {Code} for {Source}", change.Code, change.Source);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SaveAsync(AlarmChange change, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (change.alarmOn)
        {
            _logger.LogWarning("ALARM {Source} {Code}: {Message}", change.Source, change.Code, change.Message);
            db.Alarms.Add(new AlarmRecord
            {
                Source = change.Source, Code = change.Code, Message = change.Message, RaisedAt = DateTime.UtcNow,
            });
        }
        else
        {
            var open = await db.Alarms
                .Where(a => a.Source == change.Source && a.Code == change.Code && a.ClearedAt == null)
                .ToListAsync(ct);
            foreach (var a in open) a.ClearedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}