using PhoneNumbers;

namespace TelegramPanel.Core.Utils;

/// <summary>
/// 手机号格式化/规范化工具（用于展示国家码）
/// </summary>
public static class PhoneNumberFormatter
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// 仅保留数字（用于数据库存储/查询/文件名）
    /// </summary>
    public static string NormalizeToDigits(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new char[phone.Length];
        var count = 0;
        foreach (var ch in phone)
        {
            if (ch >= '0' && ch <= '9')
                digits[count++] = ch;
        }

        return count == 0 ? string.Empty : new string(digits, 0, count);
    }

    /// <summary>
    /// 格式化为 “+国家码 空格 本地号码” 的展示形式，如：+86 13800138000
    /// </summary>
    public static string FormatWithCountryCode(string? phone)
    {
        var digits = NormalizeToDigits(phone);
        if (digits.Length == 0)
            return (phone ?? string.Empty).Trim();

        try
        {
            // Telegram 账号手机号通常是 E.164 数字串（无 +），这里统一按 E.164 解析
            var number = Util.Parse("+" + digits, defaultRegion: null);
            var nsn = Util.GetNationalSignificantNumber(number);
            if (string.IsNullOrWhiteSpace(nsn))
                return "+" + digits;

            return $"+{number.CountryCode} {nsn}";
        }
        catch
        {
            // 解析失败也至少补上 "+"
            return "+" + digits;
        }
    }
}
