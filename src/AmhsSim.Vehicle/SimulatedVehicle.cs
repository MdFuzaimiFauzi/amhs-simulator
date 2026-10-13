using AmhsSim.Protocol;

namespace AmhsSim.Vehicle;

/// <summary>Faults the demo can switch on to show how the controller copes.</summary>
public sealed class FaultSettings
{
    public bool Hang { get; set; }            // stop replying, socket stays open
    public int LatencyMs { get; set; }        // delay every reply
    public bool CorruptNextReply { get; set; }
    public bool JamAxis { get; set; }         // travel stops, alarm after JamTimeout
    public bool LoseCarrier { get; set; }     // next grip finds no carrier
}

/// <summary>
/// One simulated OHT vehicle. All logic is here and has no sockets in it:
/// <see cref="Handle"/> answers a request, <see cref="Tick"/> advances time.
/// That makes it easy to unit test by calling Tick with a fixed time step.
/// </summary>
public sealed class SimulatedVehicle
{
    public const int TravelSpeedMmPerS = 3_000;
    public const int HoistSpeedMmPerS = 1_000;
    public const int HoistDepthMm = 1_500;
    public static readonly TimeSpan GripTime = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan HomeSearchTime = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan JamTimeout = TimeSpan.FromSeconds(3);

    private readonly object _lock = new();
    private VehicleState _state = VehicleState.NotHomed;
    private SubStep _step = SubStep.None;
    private double _positionMm;
    private double _hoistMm;
    private bool _carrier;
    private bool _gripper;
    private AlarmCode _alarm = AlarmCode.None;
    private int _targetMm;
    private TimeSpan _stepTimer;

    public SimulatedVehicle(string name, int startPositionMm, bool startWithCarrier = false)
    {
        Name = name;
        _positionMm = startPositionMm;
        _carrier = startWithCarrier;
        _gripper = startWithCarrier;
    }

    public string Name { get; }
    public FaultSettings Faults { get; } = new();

    /// <summary>Raised (outside the lock) whenever State, Step, carrier or alarm changes.</summary>
    public event Action<VehicleStatus>? StatusChanged;

    public VehicleStatus GetStatus()
    {
        lock (_lock) return Snapshot();
    }

    private VehicleStatus Snapshot() =>
        new(_state, _step, (int)Math.Round(_positionMm), (int)Math.Round(_hoistMm), _carrier, _gripper, _alarm);

    /// <summary>Handles one request and returns the reply frame (Status or Nak).</summary>
    public Frame Handle(Frame request)
    {
        VehicleStatus before, after;
        NakReason? nak;
        lock (_lock)
        {
            before = Snapshot();
            nak = Apply(request);
            after = Snapshot();
        }
        RaiseIfChanged(before, after);

        return nak is { } reason
            ? new Frame(MessageType.Nak, request.TxId, Payloads.Nak(reason))
            : new Frame(MessageType.Status, request.TxId, after.ToPayload());
    }

    // Must be called inside the lock. Returns a NAK reason, or null when accepted.
    private NakReason? Apply(Frame request)
    {
        switch (request.Type)
        {
            case MessageType.Heartbeat:
                return null;

            case MessageType.Home:
                if (_state is not (VehicleState.NotHomed or VehicleState.Idle)) return NakReason.NotAllowed;
                _state = VehicleState.Homing;
                _stepTimer = TimeSpan.Zero;
                return null;

            case MessageType.MoveTo:
                if (_state != VehicleState.Idle) return BusyOrNotAllowed();
                _targetMm = Payloads.ReadMoveTo(request.Payload);
                _stepTimer = TimeSpan.Zero;
                _state = VehicleState.Moving;
                return null;

            case MessageType.Acquire:
            case MessageType.Deposit:
            {
                if (_state != VehicleState.Idle) return BusyOrNotAllowed();
                var port = request.Payload.Length == 1 ? TrackLayout.FindPort(request.Payload[0]) : null;
                if (port is null || !TrackLayout.IsAt((int)_positionMm, port.PositionMm)) return NakReason.NotAllowed;
                bool acquiring = request.Type == MessageType.Acquire;
                if (acquiring == _carrier) return NakReason.NotAllowed; // can't pick when full or place when empty
                _state = acquiring ? VehicleState.Acquiring : VehicleState.Depositing;
                _step = SubStep.HoistDown;
                return null;
            }

            case MessageType.Stop:
                // Stop always wins. Stopping mid-hoist leaves the load in an unknown place: raise an alarm.
                if (_state is VehicleState.Acquiring or VehicleState.Depositing)
                    RaiseAlarm(AlarmCode.StoppedDuringHoist);
                else if (_state is VehicleState.Moving or VehicleState.Homing)
                    _state = VehicleState.Idle;
                return null;

            case MessageType.ResetAlarm:
                if (_state != VehicleState.Alarm) return NakReason.NotAllowed;
                _alarm = AlarmCode.None;
                _step = SubStep.None;
                _state = VehicleState.NotHomed; // position is no longer trusted: must home again
                return null;

            default:
                return NakReason.UnknownType;
        }
    }

    private NakReason BusyOrNotAllowed() =>
        _state is VehicleState.NotHomed or VehicleState.Alarm ? NakReason.NotAllowed : NakReason.Busy;

    /// <summary>Advances the simulation by <paramref name="dt"/>.</summary>
    public void Tick(TimeSpan dt)
    {
        VehicleStatus before, after;
        lock (_lock)
        {
            before = Snapshot();
            Step(dt);
            after = Snapshot();
        }
        RaiseIfChanged(before, after);
    }

    private void Step(TimeSpan dt)
    {
        double s = dt.TotalSeconds;
        switch (_state)
        {
            case VehicleState.Homing:
                if (_hoistMm > 0) { _hoistMm = Math.Max(0, _hoistMm - HoistSpeedMmPerS * s); break; }
                _stepTimer += dt;
                if (_stepTimer >= HomeSearchTime) _state = VehicleState.Idle;
                break;

            case VehicleState.Moving:
                if (Faults.JamAxis)
                {
                    _stepTimer += dt;
                    if (_stepTimer >= JamTimeout) RaiseAlarm(AlarmCode.AxisJam);
                    break;
                }
                double remaining = TrackLayout.ForwardDistance((int)Math.Round(_positionMm), _targetMm);
                double move = TravelSpeedMmPerS * s;
                if (remaining <= move)
                {
                    _positionMm = _targetMm;
                    _state = VehicleState.Idle;
                }
                else
                {
                    _positionMm = (_positionMm + move) % TrackLayout.LengthMm;
                }
                break;

            case VehicleState.Acquiring:
            case VehicleState.Depositing:
                StepHoistSequence(dt, _state == VehicleState.Acquiring);
                break;
        }
    }

    private void StepHoistSequence(TimeSpan dt, bool acquiring)
    {
        double s = dt.TotalSeconds;
        switch (_step)
        {
            case SubStep.HoistDown:
                _hoistMm = Math.Min(HoistDepthMm, _hoistMm + HoistSpeedMmPerS * s);
                if (_hoistMm >= HoistDepthMm)
                {
                    _step = acquiring ? SubStep.Grip : SubStep.Release;
                    _stepTimer = TimeSpan.Zero;
                }
                break;

            case SubStep.Grip:
                _stepTimer += dt;
                if (_stepTimer < GripTime) break;
                _gripper = true;
                if (Faults.LoseCarrier)
                {
                    Faults.LoseCarrier = false; // one-shot fault
                    RaiseAlarm(AlarmCode.CarrierNotSensed);
                    break;
                }
                _carrier = true;
                _step = SubStep.HoistUp;
                break;

            case SubStep.Release:
                _stepTimer += dt;
                if (_stepTimer < GripTime) break;
                _gripper = false;
                _carrier = false;
                _step = SubStep.HoistUp;
                break;

            case SubStep.HoistUp:
                _hoistMm = Math.Max(0, _hoistMm - HoistSpeedMmPerS * s);
                if (_hoistMm <= 0)
                {
                    _step = SubStep.None;
                    _state = VehicleState.Idle;
                }
                break;
        }
    }

    private void RaiseAlarm(AlarmCode code)
    {
        _alarm = code;
        _state = VehicleState.Alarm;
    }

    private void RaiseIfChanged(VehicleStatus before, VehicleStatus after)
    {
        // Position and hoist change every tick; only push on discrete changes to keep traffic low.
        bool changed = before.State != after.State || before.Step != after.Step
            || before.CarrierPresent != after.CarrierPresent || before.Alarm != after.Alarm;
        if (changed) StatusChanged?.Invoke(after);
    }
}
