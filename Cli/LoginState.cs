namespace Im.Cli;

/// <summary>
/// 轻量级登录状态机：Anonymous → Authenticated。
/// </summary>
public sealed class LoginState
{
    private string? _token;
    private string? _displayName;

    /// <summary>
    /// 是否已登录（token 非空）。
    /// </summary>
    public bool IsLoggedIn => !string.IsNullOrEmpty(_token);

    /// <summary>
    /// 显示名称，未登录时为 null。
    /// </summary>
    public string? DisplayName => _displayName;

    /// <summary>
    /// 登录令牌，未登录时为 null。
    /// </summary>
    public string? Token => _token;

    /// <summary>
    /// 尝试以给定 token 和名称完成登录。
    /// </summary>
    /// <param name="token">登录令牌，空值视为非法。</param>
    /// <param name="name">显示名称，空值存为 null。</param>
    /// <returns>登录成功返回 true，token 为空返回 false。</returns>
    public bool Authenticate(string token, string name)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        _token = token;
        _displayName = string.IsNullOrEmpty(name) ? null : name;
        return true;
    }
}
