namespace TelegramPanel.Data.Entities;

/// <summary>
/// 账号分类实体
/// </summary>
public class AccountCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// 排除操作：该分类下的账号不出现在“创建频道/批量邀请/批量设置管理员”等操作的执行账号选择中
    /// </summary>
    public bool ExcludeFromOperations { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
