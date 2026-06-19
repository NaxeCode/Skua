namespace Skua.Core.Interfaces;

public interface IScriptRunTelemetryService
{
    string? CurrentRunDirectory { get; }

    void StartRun(string scriptPath);

    void StopRun(Exception? exception = null);

    void AppendScriptLog(string message);

    void TrackEvent(string type, object? data = null);
}
