namespace AmhsSim.Engine;

/// <summary>The link to the vehicle failed (timeout, socket closed, not connected).</summary>
public sealed class VehicleLostException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The vehicle raised an alarm while working on a job.</summary>
public sealed class VehicleAlarmException(string message) : Exception(message);

/// <summary>The vehicle answered NAK.</summary>
public sealed class CommandRejectedException(string message) : Exception(message);