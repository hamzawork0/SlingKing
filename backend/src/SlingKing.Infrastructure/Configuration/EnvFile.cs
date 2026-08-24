namespace SlingKing.Infrastructure.Configuration;

/// <summary>
/// Minimal <c>.env</c> reader. Real environment variables always win, so a value
/// exported by the host (Render, CI) is never clobbered by a stray local file.
/// </summary>
public static class EnvFile
{
    public static int Load(string path)
    {
        if (!File.Exists(path)) return 0;

        var applied = 0;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) continue;

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        return applied;
    }
}
