using TelegramPanel.Data.Entities;
using TelegramPanel.Data.Repositories;

namespace TelegramPanel.Core.Services;

/// <summary>
/// 频道分组管理服务
/// </summary>
public class ChannelGroupManagementService
{
    private readonly IChannelGroupRepository _groupRepository;

    public ChannelGroupManagementService(IChannelGroupRepository groupRepository)
    {
        _groupRepository = groupRepository;
    }

    public async Task<IEnumerable<ChannelGroup>> GetAllGroupsAsync()
    {
        return await _groupRepository.GetAllAsync();
    }

    public async Task<ChannelGroup?> GetGroupAsync(int id)
    {
        return await _groupRepository.GetByIdAsync(id);
    }

    public async Task<ChannelGroup?> GetGroupByNameAsync(string name)
    {
        return await _groupRepository.GetByNameAsync(name);
    }

    public Task<bool> IsNameTakenAsync(string name, int? excludingId = null, CancellationToken cancellationToken = default) =>
        _groupRepository.NameExistsAsync(name, excludingId, cancellationToken);

    public async Task<ChannelGroup> CreateGroupAsync(ChannelGroup group)
    {
        return await _groupRepository.AddAsync(group);
    }

    public async Task UpdateGroupAsync(ChannelGroup group)
    {
        await _groupRepository.UpdateAsync(group);
    }

    public async Task DeleteGroupAsync(int id)
    {
        var group = await _groupRepository.GetByIdAsync(id);
        if (group != null)
        {
            await _groupRepository.DeleteAsync(group);
        }
    }
}
