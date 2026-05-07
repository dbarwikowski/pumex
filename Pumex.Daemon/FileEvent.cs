namespace Pumex.Daemon;

public record FileEvent(FileEventType Type, string Path);