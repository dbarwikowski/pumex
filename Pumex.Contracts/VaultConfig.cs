namespace Pumex.Contracts;

public record VaultConfig(string Name, DateTimeOffset Created, int Version)
{
    public const int CurrentVersion = 1;
}
