using System.IO;
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class SessionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mb-tests", Guid.NewGuid().ToString("N"), "browser.db");
    private readonly StorageService _storage;
    // Идентификаторы групп в сессии - Guid (таков TabGroup.Id); строка "g1" из
    // плана нечитаема и при разборе корректно отбрасывалась бы.
    private readonly string _groupId = Guid.NewGuid().ToString();

    public SessionServiceTests() => _storage = new StorageService(_dir);

    public void Dispose()
    {
        _storage.Dispose();
        var dir = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_NothingStored_ReturnsEmpty()
    {
        var (snapshot, groups, active) = new SessionService(_storage).Load();
        Assert.Null(snapshot);
        Assert.Empty(groups);
        Assert.Null(active);
    }

    [Fact]
    public void SaveThenLoad_KeepsLayoutAndActiveTab()
    {
        var service = new SessionService(_storage);

        var work = new TabGroup { Name = "Работа", ColorIndex = 4, IsCollapsed = true };
        var study = new TabGroup { Name = "Учёба", ColorIndex = 2 };
        var mail = new Tab(Guid.NewGuid());
        mail.Url = "https://mail.example/";
        var news = new Tab(Guid.NewGuid());
        var docs = new Tab(Guid.NewGuid());
        work.Tabs.Add(mail);
        work.Tabs.Add(news);
        study.Tabs.Add(docs);
        mail.IsActive = true;

        service.Save(new[] { work, study }, mail);
        var (_, groups, active) = service.Load();

        Assert.Equal(2, groups.Count);
        Assert.Equal("Работа", groups[0].Name);
        Assert.Equal(4, groups[0].ColorIndex);
        Assert.True(groups[0].IsCollapsed);
        Assert.Equal(new[] { "https://mail.example/", news.Url },
            groups[0].Tabs.Select(t => t.Url).ToArray());
        Assert.Equal(mail.Id, groups[0].Tabs[0].Id);
        Assert.Equal(new[] { mail.Id, news.Id }, groups[0].Tabs.Select(t => t.Id).ToArray());
        Assert.NotNull(active);
        Assert.Equal(mail.Id, active!.Id);
    }

    [Fact]
    public void Load_EmptyGroup_RestoresIt()
    {
        var service = new SessionService(_storage);
        var group = new TabGroup { Name = "Заготовка", ColorIndex = 1 };

        service.Save(new[] { group }, null);
        var (_, groups, active) = service.Load();

        Assert.Equal("Заготовка", Assert.Single(groups).Name);
        Assert.Empty(groups[0].Tabs);
        Assert.Null(active);
    }

    [Fact]
    public void Save_GroupColorOutOfPalette_IsClampedOnLoad()
    {
        var service = new SessionService(_storage);
        var group = new TabGroup { Name = "А", ColorIndex = 42 };
        group.Tabs.Add(new Tab { Url = "https://a.example/" });

        service.Save(new[] { group }, null);
        var (_, groups, _) = service.Load();

        Assert.InRange(groups[0].ColorIndex, 0, 7);
    }

    [Fact]
    public void Load_TabsWithUnknownGroup_AreSkipped()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow(_groupId, "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow(Guid.NewGuid().ToString(), _groupId, "https://a.example/", "А", false, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), "нет-такой-группы", "https://b.example/", "Б", false, 1),
            }));

        var (_, groups, _) = new SessionService(_storage).Load();

        Assert.Single(groups);
        Assert.Equal("https://a.example/", Assert.Single(groups[0].Tabs).Url);
    }

    [Fact]
    public void Load_MultipleActiveRows_KeepsFirstByPosition()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow(_groupId, "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow(Guid.NewGuid().ToString(), _groupId, "https://a.example/", "А", true, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), _groupId, "https://b.example/", "Б", true, 1),
            }));

        var (_, _, active) = new SessionService(_storage).Load();

        Assert.NotNull(active);
        Assert.Equal("https://a.example/", active!.Url);
    }

    [Fact]
    public void Load_UnparsableId_SkipsRow()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow(_groupId, "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow("не-guid", _groupId, "https://a.example/", "А", false, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), _groupId, "https://b.example/", "Б", false, 1),
            }));

        var (_, groups, _) = new SessionService(_storage).Load();

        Assert.Equal("https://b.example/", Assert.Single(groups[0].Tabs).Url);
    }

    [Fact]
    public void Load_AllRowsUnusable_ReturnsEmpty()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("не-guid", "А", 0, false, 0) },
            new[] { new SessionTabRow(Guid.NewGuid().ToString(), "не-guid", "https://a.example/", "", false, 0) }));

        var (_, groups, active) = new SessionService(_storage).Load();

        Assert.Empty(groups);
        Assert.Null(active);
    }
}