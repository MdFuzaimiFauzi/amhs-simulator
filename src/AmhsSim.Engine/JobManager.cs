using System.Collections.Concurrent; //allow multithread
using Microsoft.EntityFrameworkCore;
using AmhsSim.Engine.Data;
using AmhsSim.Protocol;

namespace AmhsSim.Engine;

/// <summary>A job that has a vehicle and a runner task working on it.</summary>
public sealed class ActiveJob(int id, string vehicleName, JobStateMachine machine)
{
    public int Id => id;
    public string VehicleName => vehicleName;
    public JobStateMachine Machine => machine;
    public CancellationTokenSource Cts { get; } = new();
    public bool AbortRequested { get; set; }
}

/// <summary>
/// Dispatches queued jobs to vehicles and runs each job step by step.
/// Only a job's own runner fires its triggers, so there is one writer per job and no lock is needed.
/// </summary>
public sealed class JobManager(
    VehicleManager vehicles,
    AlarmService alarms,
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<JobManager> logger) : BackgroundService
{
    private static readonly TimeSpan DispatchInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<int, ActiveJob> _active = new();

    public event Action? Changed;

    public bool IsBusy(string vehicleName) => _active.Values.Any(j => j.VehicleName == vehicleName);

    public async Task<TransferJob> CreateAsync(string carrierId, string source, string dest, int priority)
    {
        if (string.IsNullOrWhiteSpace(carrierId)) throw new ArgumentException("Carrier id is required");
        if (TrackLayout.FindPort(source) is null) throw new ArgumentException($"Unknown port {source}");
        if (TrackLayout.FindPort(dest) is null) throw new ArgumentException($"Unknown port {dest}");
        if (source.Equals(dest, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Source and destination are the same");

        await using var db = await dbFactory.CreateDbContextAsync();
        var job = new TransferJob
        {
            CarrierId = carrierId.Trim(), SourcePort = source, DestPort = dest, Priority = priority,
            State = JobState.Queued, CreatedAt = DateTime.UtcNow,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        logger.LogInformation("Job {Id} created: {Carrier} {Source} -> {Dest}", job.Id, job.CarrierId, source, dest);
        Changed?.Invoke();
        return job;
    }

    public async Task CancelAsync(int jobId, bool abort)
    {
        if (_active.TryGetValue(jobId, out var active))
        {
            active.AbortRequested = abort;
            active.Cts.Cancel(); // the runner sees this and fires Cancel or Abort itself
            return;
        }

        // Not running yet: it can only be Queued, which we cancel directly.
        await using var db = await dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(jobId);
        if (job?.State != JobState.Queued) return;
        await TransitionAsync(jobId, new JobStateMachine(JobState.Queued), JobTrigger.Cancel, "cancelled while queued");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await RecoverAfterRestartAsync();

        using var timer = new PeriodicTimer(DispatchInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { await DispatchAsync(ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Dispatch failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Jobs that were mid-flight when the controller stopped can't be trusted: abort them.</summary>
    private async Task RecoverAfterRestartAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var stale = await db.Jobs
            .Where(j => j.State != JobState.Queued && j.State != JobState.Completed
                        && j.State != JobState.Cancelled && j.State != JobState.Aborted)
            .ToListAsync();
        foreach (var job in stale)
        {
            db.JobEvents.Add(new JobEvent
            {
                TransferJobId = job.Id, From = job.State, To = JobState.Aborted, Trigger = JobTrigger.Abort,
                At = DateTime.UtcNow, Note = "controller restarted",
            });
            job.State = JobState.Aborted;
            job.FinishedAt = DateTime.UtcNow;
            job.Note = "controller restarted";
        }
        await db.SaveChangesAsync();
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var queued = await db.Jobs.AsNoTracking()
            .Where(j => j.State == JobState.Queued)
            .OrderByDescending(j => j.Priority).ThenBy(j => j.Id)
            .ToListAsync(ct);

        foreach (var job in queued)
        {
            var source = TrackLayout.FindPort(job.SourcePort)!;
            var candidates = vehicles.Connections.Select(v => new VehicleCandidate(v.Name, v.IsOnline, IsBusy(v.Name), v.Status));
            string? vehicleName = VehiclePicker.PickNearest(candidates, source.PositionMm);
            if (vehicleName is null) return; // no free vehicle: higher priority jobs keep their place

            var active = new ActiveJob(job.Id, vehicleName, new JobStateMachine(JobState.Queued));
            _active[job.Id] = active;
            await TransitionAsync(job.Id, active.Machine, JobTrigger.Assign, $"assigned to {vehicleName}", vehicleName);
            _ = Task.Run(() => RunJobAsync(job, active, vehicles.Find(vehicleName)!), CancellationToken.None);
        }
    }

    private async Task RunJobAsync(TransferJob job, ActiveJob active, VehicleConnection v)
    {
        var src = TrackLayout.FindPort(job.SourcePort)!;
        var dst = TrackLayout.FindPort(job.DestPort)!;
        var m = active.Machine;
        var ct = active.Cts.Token;

        try
        {
            await TransitionAsync(job.Id, m, JobTrigger.Start);
            await CommandAsync(v, MessageType.MoveTo, Payloads.MoveTo(src.PositionMm), ct);
            await WaitForAsync(v, s => s.State == VehicleState.Idle && TrackLayout.IsAt(s.PositionMm, src.PositionMm), ct);

            await TransitionAsync(job.Id, m, JobTrigger.ArrivedAtSource);
            await CommandAsync(v, MessageType.Acquire, Payloads.Port(src.Id), ct);
            await WaitForAsync(v, s => s.State == VehicleState.Idle && s.CarrierPresent, ct);
            m.CarrierOnBoard = true;

            await TransitionAsync(job.Id, m, JobTrigger.CarrierOnBoard);
            await CommandAsync(v, MessageType.MoveTo, Payloads.MoveTo(dst.PositionMm), ct);
            await WaitForAsync(v, s => s.State == VehicleState.Idle && TrackLayout.IsAt(s.PositionMm, dst.PositionMm), ct);

            await TransitionAsync(job.Id, m, JobTrigger.ArrivedAtDest);
            await CommandAsync(v, MessageType.Deposit, Payloads.Port(dst.Id), ct);
            await WaitForAsync(v, s => s.State == VehicleState.Idle && !s.CarrierPresent, ct);
            m.CarrierOnBoard = false;

            await TransitionAsync(job.Id, m, JobTrigger.CarrierOff);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryStopAsync(v);
            var trigger = active.AbortRequested || !m.CanFire(JobTrigger.Cancel) ? JobTrigger.Abort : JobTrigger.Cancel;
            await FinishAsync(job, m, trigger, trigger == JobTrigger.Cancel ? "cancelled by operator" : "aborted by operator");
        }
        catch (VehicleLostException ex) when (m.CanFire(JobTrigger.VehicleLost))
        {
            await TransitionAsync(job.Id, m, JobTrigger.VehicleLost, $"re-queued: {ex.Message}", vehicleName: null);
        }
        catch (Exception ex)
        {
            await FinishAsync(job, m, JobTrigger.Abort, ex.Message);
        }
        finally
        {
            _active.TryRemove(job.Id, out _);
            active.Cts.Dispose();
            Changed?.Invoke();
        }
    }

    private async Task FinishAsync(TransferJob job, JobStateMachine m, JobTrigger trigger, string note)
    {
        if (!m.CanFire(trigger)) return;
        await TransitionAsync(job.Id, m, trigger, note);
        if (trigger == JobTrigger.Abort)
            alarms.Raise($"Job {job.Id}", "JobAborted", $"{job.CarrierId} {job.SourcePort}->{job.DestPort}: {note}");
    }

    private static async Task CommandAsync(VehicleConnection v, MessageType type, byte[] payload, CancellationToken ct)
    {
        Frame reply;
        try
        {
            reply = await v.SendAsync(type, payload, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            throw new VehicleLostException(ex.Message, ex);
        }
        if (reply.Type == MessageType.Nak)
            throw new CommandRejectedException($"{v.Name} rejected {type}: {(NakReason)reply.Payload[0]}");
    }

    /// <summary>Polls the vehicle's latest status until the condition holds, an alarm appears, or the link drops.</summary>
    private static async Task WaitForAsync(VehicleConnection v, Func<VehicleStatus, bool> done, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!v.IsOnline) throw new VehicleLostException($"{v.Name} went offline");
            if (v.Status is { } s)
            {
                if (s.State == VehicleState.Alarm) throw new VehicleAlarmException($"{v.Name} alarm {s.Alarm}");
                if (done(s)) return;
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{v.Name} step took longer than {StepTimeout.TotalSeconds:0} s");
            await Task.Delay(100, ct);
        }
    }

    private async Task TryStopAsync(VehicleConnection v)
    {
        try { await v.SendAsync(MessageType.Stop, Payloads.Empty); }
        catch (Exception ex) { logger.LogWarning("Stop to {Vehicle} failed: {Reason}", v.Name, ex.Message); }
    }

    /// <summary>Fires the trigger, then saves the new state and a JobEvent row in one SaveChanges.</summary>
    private async Task TransitionAsync(int jobId, JobStateMachine m, JobTrigger trigger, string? note = null, string? vehicleName = "")
    {
        var from = m.State;
        m.Fire(trigger);
        var to = m.State;

        await using var db = await dbFactory.CreateDbContextAsync();
        var job = await db.Jobs.FindAsync(jobId) ?? throw new InvalidOperationException($"Job {jobId} not found");
        job.State = to;
        if (vehicleName != "") job.VehicleName = vehicleName; // "" means keep, null means clear
        if (note is not null) job.Note = note;
        if (m.IsFinished) job.FinishedAt = DateTime.UtcNow;
        db.JobEvents.Add(new JobEvent { TransferJobId = jobId, From = from, To = to, Trigger = trigger, At = DateTime.UtcNow, Note = note });
        await db.SaveChangesAsync();

        logger.LogInformation("Job {Id}: {From} -> {To} on {Trigger} {Note}", jobId, from, to, trigger, note);
        Changed?.Invoke();
    }
}