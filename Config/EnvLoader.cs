namespace Im.Config;

/// <summary>
/// 应用程序配置加载器，从 .env 文件读取配置。
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// 从 .env 文件中加载 HOST 和 PORT 配置。
    /// 优先搜索 AppContext.BaseDirectory，然后回退到当前工作目录。
    /// 如果文件不存在则返回默认配置。
    /// </summary>
    public static AppConfig Load()
    {
        var config = new AppConfig();

        string envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (!File.Exists(envPath))
        {
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        }

        if (!File.Exists(envPath))
        {
            Console.WriteLine("[WARN] 未找到 .env 文件，使用默认配置：HOST=127.0.0.1, PORT=12345");
            return config;
        }

        Console.WriteLine($"[INFO] 正在加载配置：{envPath}");

        foreach (string line in File.ReadAllLines(envPath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            int eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0) continue;

            string key = trimmed[..eqIndex].Trim().ToUpperInvariant();
            string value = trimmed[(eqIndex + 1)..].Trim().Trim('"', '\'');

            switch (key)
            {
                case "HOST":
                    config.Host = value;
                    break;
                case "PORT":
                    if (int.TryParse(value, out int port) && port is > 0 and < 65536)
                        config.Port = port;
                    else
                        Console.WriteLine($"[WARN] PORT 值 '{value}' 无效，使用默认值：{config.Port}");
                    break;
                case "CONV":
                    if (TryParseUint(value, out uint conv))
                        config.Conv = conv;
                    else
                        Console.WriteLine($"[WARN] CONV 值 '{value}' 无效，使用默认值：0x{config.Conv:x8}");
                    break;
            }
        }

        Console.WriteLine($"[INFO] 配置加载完成：HOST={config.Host}, PORT={config.Port}, CONV=0x{config.Conv:x8}");
        return config;
    }

    /// <summary>
    /// 尝试将字符串解析为 uint，支持十进制和 0x 十六进制格式。
    /// </summary>
    private static bool TryParseUint(string value, out uint result)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out result);

        return uint.TryParse(value, out result);
    }
}
