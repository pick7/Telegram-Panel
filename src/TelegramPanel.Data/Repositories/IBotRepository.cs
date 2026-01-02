using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

public interface IBotRepository : IRepository<Bot>
{
    Task<Bot?> GetByNameAsync(string name);
    Task<Bot?> GetByTokenAsync(string token);
    Task<IEnumerable<Bot>> GetAllWithStatsAsync();
}

