using System.IO;

namespace MiniBrowser.Services.Import;

/// <summary>
/// Временная копия файлов чужого профиля: все чтения идут из этой папки,
/// открытый браузер не блокирует импорт. Удаляется вместе с содержимым.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Dir { get; } = Path.Combine(
        Path.GetTempPath(), "MiniBrowserImport-" + Guid.NewGuid().ToString("N"));

    public TempWorkspace() => Directory.CreateDirectory(Dir);

    /// <summary>Скопировать файл профиля; "" — файла нет.</summary>
    public string CopyShared(string fileName, string profileDir)
    {
        var source = Path.Combine(profileDir, fileName);
        if (!File.Exists(source)) return "";
        var target = Path.Combine(Dir, fileName);
        // Общий доступ на чтение: файл может быть открыт работающим браузером.
        using var input = File.Open(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(target);
        input.CopyTo(output);
        return target;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch { /* времёнка и так умрёт с системой */ }
    }
}
