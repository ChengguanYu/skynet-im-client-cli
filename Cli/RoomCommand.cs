namespace Im.Cli;

/// <summary>
/// 房间占位命令：room list / room entry &lt;room&gt; / room create -n &lt;name&gt;。
/// </summary>
public static class RoomCommand
{
    /// <summary>
    /// 执行 room 子命令。
    /// </summary>
    /// <param name="login">当前登录状态。</param>
    /// <param name="input">原始用户输入（以 room 开头）。</param>
    /// <param name="ct">取消令牌（当前未使用）。</param>
    /// <returns>始终返回 true，不退出程序。</returns>
    public static Task<bool> ExecuteAsync(LoginState login, string input, CancellationToken ct)
    {
        if (!login.IsLoggedIn)
        {
            Console.WriteLine("[ERROR] 请先 connect 登录");
            return Task.FromResult(true);
        }

        string trimmed = input.Trim();
        string rest = trimmed.Length >= 4 ? trimmed[4..].Trim() : "";

        if (string.IsNullOrEmpty(rest))
        {
            PrintUsage();
            return Task.FromResult(true);
        }

        int spaceIdx = rest.IndexOf(' ');
        string sub = (spaceIdx < 0 ? rest : rest[..spaceIdx]).ToLowerInvariant();
        string arg = spaceIdx < 0 ? "" : rest[(spaceIdx + 1)..].Trim();

        switch (sub)
        {
            case "list":
                Console.WriteLine("[INFO] room list：列出房间列表（业务待实现）");
                return Task.FromResult(true);

            case "entry":
                if (string.IsNullOrEmpty(arg))
                {
                    Console.WriteLine("用法: room entry <room>");
                    return Task.FromResult(true);
                }
                Console.WriteLine($"[INFO] room entry {arg}：进入房间（业务待实现）");
                return Task.FromResult(true);

            case "create":
                string? roomName = ParseNameOption(arg);
                if (string.IsNullOrEmpty(roomName))
                {
                    Console.WriteLine("用法: room create -n <name>");
                    return Task.FromResult(true);
                }
                Console.WriteLine($"[INFO] room create {roomName}：创建房间（业务待实现）");
                return Task.FromResult(true);

            default:
                PrintUsage();
                return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 从参数中解析 -n 选项的值，保留原始大小写。
    /// </summary>
    private static string? ParseNameOption(string arg)
    {
        var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i] == "-n" && i + 1 < tokens.Length)
                return tokens[i + 1];
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法: room list | room entry <room> | room create -n <name>");
    }
}
