using System.Collections;

namespace SlingKing.Infrastructure.Configuration;

/// <summary>
/// Minimal <c>.env</c> reader. A variable already present in the process environment always
/// wins — including one exported but set to an empty string — so a value deliberately set by
/// the host (Render, CI) is never overridden by a stray local file. Note: on Windows,
/// <see cref="Environment.SetEnvironmentVariable(string, string?)"/> treats an empty-string
/// value as "unset" rather than "present but empty", so that particular state cannot be
/// reproduced through that API on this platform; the presence check below is still correct on
/// the Linux hosts this runs on in production.
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

            if (Environment.GetEnvironmentVariables().Contains(key)) continue;

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        return applied;
    }
}
