using TelegramPanel.Data.Entities;

namespace TelegramPanel.Data.Repositories;

public interface IBotChannelCategoryRepository : IRepository<BotChannelCategory>
{
    /// <summary>
    /// 兼容旧模块：按 Bot 获取分类列表。
    /// 由于分类已全局化，该方法当前等价于返回全部分类。
    /// </summary>
    Task<IEnumerable<BotChannelCategory>> GetForBotAsync(int botId);

    Task<IEnumerable<BotChannelCategory>> GetAllOrderedAsync();
    Task<BotChannelCategory?> GetByNameAsync(string name);
}
