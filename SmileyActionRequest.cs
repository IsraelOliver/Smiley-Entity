namespace Smiley;

public enum SmileyActionType { None, Idle, Walk, Run, Jump, Explore }
// No model-supplied coordinates, speeds, durations, method names or executable payloads.
public readonly record struct SmileyActionRequest(SmileyActionType Type);
public readonly record struct SmileyActionResult(bool Accepted, string Message);
public readonly record struct SmileyActionEnvironment(WorkArea Area, double Width, double ScaleX, double ScaleY);
