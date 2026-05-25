using Pumex.Contracts;

namespace Pumex.Daemon;

public interface IVaultRepository
{
    Task<long> AddVaultAsync(string name, string path);
    Task<List<VaultRecord>> GetVaultsAsync();
    Task<VaultRecord?> GetVaultByPathAsync(string path);
    Task<VaultRecord?> GetVaultByNameAsync(string name);
    Task RemoveVaultAsync(long vaultId);
    Task<List<TagCount>> GetTagsAsync(long? vaultId = null);
}
