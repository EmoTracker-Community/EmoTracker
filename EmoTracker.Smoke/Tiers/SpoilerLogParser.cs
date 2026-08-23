using System.Text.Json;

namespace EmoTracker.Smoke;

/// <summary>
/// A single step of a seed playthrough.
/// </summary>
public sealed record SpoilerPickup(int Sphere, string Location, string Item);

/// <summary>
/// Parses the SMZ3 randomizer spoiler log playthrough section.
///
/// Format (v11+):
///   - Playthrough:
///     - Sphere 1:
///         Eastern Palace - Map Chest: Reserve Tank
///         ...
///   - Prizes and Requirements: ...
/// Individual lines are "Location: Item" (Location may be quoted).
/// </summary>
public static class SpoilerLogParser
{
    public static List<SpoilerPickup> ParsePlaythrough(string path)
    {
        var result = new List<SpoilerPickup>();
        if (!File.Exists(path))
            throw new FileNotFoundException($"Spoiler log not found: {path}", path);

        bool inPlay = false;
        int sphere = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("- Playthrough:", StringComparison.Ordinal))
            {
                inPlay = true;
                continue;
            }
            if (inPlay && line.StartsWith("- Prizes and Requirements:", StringComparison.Ordinal))
                break;
            if (!inPlay) continue;

            var sm = System.Text.RegularExpressions.Regex.Match(line, @"^\s*- Sphere (\d+):\s*$");
            if (sm.Success) { sphere = int.Parse(sm.Groups[1].Value); continue; }

            // Location may be double-quoted, e.g. "Energy Tank, Wrecked Ship": Ice Beam
            var pm = System.Text.RegularExpressions.Regex.Match(line, @"^\s+(?:\""?([^\""]+?)\""?)?:\s*(.+?)\s*$");
            if (pm.Success && sphere > 0)
            {
                string loc = pm.Groups[1].Value.Trim();
                string item = pm.Groups[2].Value.Trim();
                if (loc.Length > 0 && item.Length > 0)
                    result.Add(new SpoilerPickup(sphere, loc, item));
            }
        }
        return result;
    }
}
