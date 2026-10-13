using AmhsSim.Protocol;
using AmhsSim.Vehicle;

namespace AmhsSim.Tests;

public class SimulatedVehicleTests
{
    private static readonly TimeSpan Dt = TimeSpan.FromMilliseconds(100);

    private static Frame Send(SimulatedVehicle v, MessageType type, byte[]? payload = null) =>
        v.Handle(new Frame(type, 1, payload ?? []));

    private static void RunFor(SimulatedVehicle v, TimeSpan time)
    {
        for (var t = TimeSpan.Zero; t < time; t += Dt) v.Tick(Dt);
    }

    private static SimulatedVehicle HomedVehicleAt(int positionMm)
    {
        var v = new SimulatedVehicle("T", positionMm);
        Send(v, MessageType.Home);
        RunFor(v, TimeSpan.FromSeconds(2));
        Assert.Equal(VehicleState.Idle, v.GetStatus().State);
        return v;
    }

    [Fact]
    public void Move_is_rejected_before_homing()
    {
        var v = new SimulatedVehicle("T", 0);
        var reply = Send(v, MessageType.MoveTo, Payloads.MoveTo(10_000));
        Assert.Equal(MessageType.Nak, reply.Type);
        Assert.Equal((byte)NakReason.NotAllowed, reply.Payload[0]);
    }

    [Fact]
    public void Move_reaches_target_and_goes_idle()
    {
        var v = HomedVehicleAt(0);
        Send(v, MessageType.MoveTo, Payloads.MoveTo(6_000)); // 2s at 3 m/s
        RunFor(v, TimeSpan.FromSeconds(2.5));

        var s = v.GetStatus();
        Assert.Equal(VehicleState.Idle, s.State);
        Assert.Equal(6_000, s.PositionMm);
    }

    [Fact]
    public void Acquire_runs_hoist_down_grip_hoist_up()
    {
        var port = TrackLayout.FindPort("A");
        var v = HomedVehicleAt(port.PositionMm);
        var steps = new List<SubStep>();
        v.StatusChanged += s => steps.Add(s.Step);

        Send(v, MessageType.Acquire, Payloads.Port(port.Id));
        RunFor(v, TimeSpan.FromSeconds(5));

        Assert.True(v.GetStatus().CarrierPresent);
        Assert.Equal(VehicleState.Idle, v.GetStatus().State);
        Assert.Equal([SubStep.HoistDown, SubStep.Grip, SubStep.HoistUp, SubStep.None], steps);
    }

    [Fact]
    public void Jammed_axis_raises_alarm()
    {
        var v = HomedVehicleAt(0);
        v.Faults.JamAxis = true;
        Send(v, MessageType.MoveTo, Payloads.MoveTo(30_000));
        RunFor(v, SimulatedVehicle.JamTimeout + Dt);

        Assert.Equal(VehicleState.Alarm, v.GetStatus().State);
        Assert.Equal(AlarmCode.AxisJam, v.GetStatus().Alarm);
    }

    [Fact]
    public void Stop_during_hoist_with_alarm_and_reset_requires_homing()
    {
        var port = TrackLayout.FindPort("A");
        var v = HomedVehicleAt(port.PositionMm);
        Send(v, MessageType.Acquire, Payloads.Port(port.Id));
        RunFor(v, TimeSpan.FromMilliseconds(500)); // mid-hoist
        
        Send(v, MessageType.Stop); 
        Assert.Equal(AlarmCode.StoppedDuringHoist, v.GetStatus().Alarm);

        Send(v, MessageType.ResetAlarm);
        Assert.Equal(VehicleState.NotHomed, v.GetStatus().State);
    }




}