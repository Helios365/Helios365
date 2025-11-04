namespace Helios365.Core.Repositories;

public interface IRepository<T> where T : class
{
    Task<T?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<T> CreateAsync(T item, CancellationToken cancellationToken = default);
    Task<T> UpdateAsync(string id, T item, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> ListAsync(int limit = 100, int offset = 0, CancellationToken cancellationToken = default);
}
