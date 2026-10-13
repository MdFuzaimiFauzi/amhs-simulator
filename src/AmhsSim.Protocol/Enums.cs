namespace AmhsSim.Protocol;

/// <summary> Byte 1 of every frame: what the frame means. </summary>
public enum MessageType : byte
{
    Heartbeat = 0x01,
    Status = 0x02,
    MoveTo = 0x10,
    Acquire = 0x11,
    Deposit = 0x12,
    Stop = 0x13,
    Home = 0x14,

    ResetAlarm = 0x15,
    Nak = 0x7F,
}

public enum VehicleState : byte
{
    NotHomed,
    Homing,
    Idle,
    Moving,
    Acquiring,
    Depositing,
    Alarm,
}

/// <summary> Where a vehicle is inside an Acquire or Deposit Sequence.</summary>
public enum SubStep : byte
{
    None,
    HoistDown,
    Grip,
    Release,
    HoistUp,
}

public enum AlarmCode : byte
{
    None,
    AxisJam,
    CarrierNotSensed,
    StoppedDuringHoist,
}

public enum NakReason : byte
{
    Busy = 1,
    NotAlllowed = 2,
    UnknownType = 3,
}