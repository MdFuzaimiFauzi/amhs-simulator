using AmhsSim.Engine;

namespace AmhsSim.Engine.Data;

public sealed class TransferJob
{
    public int Id { get; set; }
    public string CarrierId { get; set; } = "";
    public string SourcePort { get; set; } = "";
    public string DestPort { get; set; } = "";
    public int Priority { get; set; }
    public JobState State { get; set; } = JobState.Queued;
    public string? VehicleName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Note { get; set; }
    public List<JobEvent> Events { get; set; } = [];
}

/// <summary>One row per state transition: the job's audit trail.</summary>
public sealed class JobEvent
{
    public int Id { get; set; }
    public int TransferJobId { get; set; }
    public JobState From { get; set; }
    public JobState To { get; set; }
    public JobTrigger Trigger { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

public sealed class AlarmRecord
{
    public int Id { get; set; }
    public string Source { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedAt { get; set; }
    public DateTime? ClearedAt { get; set; }
}