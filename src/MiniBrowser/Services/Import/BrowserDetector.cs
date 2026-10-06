using System.IO;

namespace MiniBrowser.Services.Import;

/// <summary>
/// Находит установленные браузеры по стандартным путям профилей.
/// Ничего не читает из чужих данных — только проверяет наличие каталогов;
/// сам импорт начинается по кнопке пользователя.
/// </summary>
public static class BrowserDetector
{
    /// <summary>Порядок: Firefox, Chrome, Edge. Отсутствующие браузеры просто не попадают в список.</summary>
    public static IReadOnlyList<BrowserProfile> Detect()
    {
        var result = new List<BrowserProfile>();
        DetectFirefox(result);
        DetectChromium(result, BrowserKind.Chrome, "Google", "Chrome", "Google Chrome");
        DetectChromium(result, BrowserKind.Edge, "Microsoft", "Edge", "Microsoft Edge");
        return result;
    }

    private static void DetectFirefox(List<BrowserProfile> result)
    {
        // Профили Firefox описаны в profiles.ini; при IsRelative=1 путь относителен к папке Firefox.
        var firefoxDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox");
        var iniPath = Path.Combine(firefoxDir, "profiles.ini");
        if (!File.Exists(iniPath)) return;

        var sections = ParseIni(iniPath);

        var profiles = new List<(string FullPath, Dictionary<string, string> Vars)>();
        foreach (var (name, vars) in sections)
        {
            if (!name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)
                || !vars.TryGetValue("Path", out var path))
                continue;
            profiles.Add((Resolve(firefoxDir, path, IsTrue(vars.GetValueOrDefault("IsRelative"))), vars));
        }
        if (profiles.Count == 0) return;

        // Приоритет: активный профиль из [Install...]-секции → Default=1 → первый существующий.
        string? chosen = null;
        foreach (var (name, vars) in sections)
        {
            if (!name.StartsWith("Install", StringComparison.OrdinalIgnoreCase)
                || !vars.TryGetValue("Default", out var installPath))
                continue;
            var full = Resolve(firefoxDir, installPath, isRelative: true);
            if (Directory.Exists(full))
            {
                chosen = full;
                break;
            }
        }
        if (chosen is null)
            foreach (var (full, vars) in profiles)
                if (IsTrue(vars.GetValueOrDefault("Default")))
                {
                    chosen = full;
                    break;
                }
        if (chosen is null)
            chosen = profiles.Select(p => p.FullPath).FirstOrDefault(Directory.Exists);

        if (chosen is null || !Directory.Exists(chosen)) return;
        result.Add(new BrowserProfile(BrowserKind.Firefox, "Mozilla Firefox", chosen, LocalStateFile: null));
    }

    private static void DetectChromium(
        List<BrowserProfile> result, BrowserKind kind, string vendor, string product, string displayName)
    {
        // У Chromium пароли шифруются ключом из Local State (корень User Data), закладки — в профиле.
        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), vendor, product, "User Data");
        var profileDir = Path.Combine(userData, "Default");
        if (!Directory.Exists(profileDir)) return;
        var localState = Path.Combine(userData, "Local State");
        result.Add(new BrowserProfile(kind, displayName, profileDir,
            LocalStateFile: File.Exists(localState) ? localState : null));
    }

    private static string Resolve(string baseDir, string path, bool isRelative) =>
        !isRelative || Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));

    /// <summary>Простой разбор ini: секции [Имя], ключ=значение, «;» и «#» — комментарии.</summary>
    private static List<(string Name, Dictionary<string, string> Vars)> ParseIni(string path)
    {
        var sections = new List<(string Name, Dictionary<string, string> Vars)>();
        Dictionary<string, string>? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                sections.Add((line[1..^1], current));
                continue;
            }
            if (current is null) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return sections;
    }

    private static bool IsTrue(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
