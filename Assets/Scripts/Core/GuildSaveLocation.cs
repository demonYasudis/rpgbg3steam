using System;
using System.IO;

namespace GuildTactics.Core
{
    /// <summary>Development-player acceptance runs can use an isolated save without touching the user's guild.</summary>
    public static class GuildSaveLocation
    {
        public static string Resolve(string persistentDirectory, string[] arguments, bool development)
        {
            if (development)
                for (int i = 0; i < arguments.Length; i++)
                    if (arguments[i] == "--qa-save")
                    {
                        if (i + 1 >= arguments.Length || !Path.IsPathRooted(arguments[i + 1]))
                            throw new ArgumentException("--qa-save requires an absolute file path.");
                        return Path.GetFullPath(arguments[i + 1]);
                    }
            return Path.Combine(persistentDirectory, "guild-v1.json");
        }
    }
}
