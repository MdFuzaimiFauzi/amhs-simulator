using Stateless;

namespace AmhsSim.Engine;

public enum JobState { Queued, Assigned, MovingToSource, Acquiring, MovingToDest, Depositing, Completed, Cancelled, Aborted }

public enum JobTrigger { Assign, Start, ArrivedAtSource, CarrierOnBoard, ArrivedAtDest, CarrierOff, Cancel, Abort, VehicleLost }

/// <summary>
/// The rules for a transfer job's life. It only decides which transitions are legal;
/// the JobManager does the actual work and fires the triggers.
/// </summary>
public sealed class JobStateMachine
{
    private readonly StateMachine<JobState, JobTrigger> _machine;

    public JobStateMachine(JobState initial = JobState.Queued)
    {
        State = initial;
        _machine = new StateMachine<JobState, JobTrigger>(() => State, s => State = s);

        _machine.Configure(JobState.Queued)
            .Permit(JobTrigger.Assign, JobState.Assigned)
            .Permit(JobTrigger.Cancel, JobState.Cancelled);

        _machine.Configure(JobState.Assigned)
            .Permit(JobTrigger.Start, JobState.MovingToSource)
            .Permit(JobTrigger.Cancel, JobState.Cancelled)
            .Permit(JobTrigger.VehicleLost, JobState.Queued);

        _machine.Configure(JobState.MovingToSource)
            .Permit(JobTrigger.ArrivedAtSource, JobState.Acquiring)
            .Permit(JobTrigger.Cancel, JobState.Cancelled)
            .Permit(JobTrigger.Abort, JobState.Aborted)
            .Permit(JobTrigger.VehicleLost, JobState.Queued);

        // From Acquiring on the carrier may be on board, so the job can only be aborted, not cancelled.
        _machine.Configure(JobState.Acquiring)
            .Permit(JobTrigger.CarrierOnBoard, JobState.MovingToDest)
            .Permit(JobTrigger.Abort, JobState.Aborted)
            .PermitIf(JobTrigger.VehicleLost, JobState.Queued, () => !CarrierOnBoard, "carrier not picked yet");

        _machine.Configure(JobState.MovingToDest)
            .Permit(JobTrigger.ArrivedAtDest, JobState.Depositing)
            .Permit(JobTrigger.Abort, JobState.Aborted);

        _machine.Configure(JobState.Depositing)
            .Permit(JobTrigger.CarrierOff, JobState.Completed)
            .Permit(JobTrigger.Abort, JobState.Aborted);
    }

    public JobState State { get; private set; }

    /// <summary>Set by the job when the vehicle reports the carrier.</summary>
    public bool CarrierOnBoard { get; set; }

    public bool IsFinished => State is JobState.Completed or JobState.Cancelled or JobState.Aborted;

    public bool CanFire(JobTrigger trigger) => _machine.CanFire(trigger);

    public void Fire(JobTrigger trigger) => _machine.Fire(trigger);

    public string ToDotGraph() => Stateless.Graph.UmlDotGraph.Format(_machine.GetInfo());
}