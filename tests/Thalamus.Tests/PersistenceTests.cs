using Thalamus.Core.Models;
using Thalamus.Core.Persistence;

namespace Thalamus.Tests;

[TestClass]
public sealed class PersistenceTests
{
    [TestMethod]
    public async Task AtomicFileRecoversFromCorruptCurrentFile()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var file = new AtomicJsonFile<ThalamusSettings>(path);
            var first = new ThalamusSettings { ThemePreset = "Graphite" };
            var second = new ThalamusSettings { ThemePreset = "Frost" };
            await file.SaveAsync(first);
            await file.SaveAsync(second);
            await File.WriteAllTextAsync(path, "{corrupt");

            var recovered = await file.LoadAsync();

            Assert.IsNotNull(recovered);
            Assert.AreEqual("Graphite", recovered.ThemePreset);
            var repaired = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(path));
            Assert.IsNotNull(repaired);
            Assert.AreEqual("Graphite", repaired.ThemePreset);
            var preservedBackup = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(file.BackupPath));
            Assert.IsNotNull(preservedBackup);
            Assert.AreEqual("Graphite", preservedBackup.ThemePreset);

            await File.WriteAllTextAsync(path, "{corrupt-again");
            var recoveredAgain = await file.LoadAsync();
            Assert.IsNotNull(recoveredAgain);
            Assert.AreEqual("Graphite", recoveredAgain.ThemePreset);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task AtomicFileRecoversWhenCurrentJsonIsNull()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var file = new AtomicJsonFile<ThalamusSettings>(path);
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Graphite" });
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Frost" });
            await File.WriteAllTextAsync(path, "null");

            var recovered = await file.LoadAsync();

            Assert.IsNotNull(recovered);
            Assert.AreEqual("Graphite", recovered.ThemePreset);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task RepairWritePreservesLastKnownGoodBackup()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var file = new AtomicJsonFile<ThalamusSettings>(path);
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Graphite" });
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Frost" });

            await file.RepairAsync(new ThalamusSettings { ThemePreset = "Krypton Glass" });

            var current = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(path));
            var backup = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(file.BackupPath));
            Assert.IsNotNull(current);
            Assert.IsNotNull(backup);
            Assert.AreEqual("Krypton Glass", current.ThemePreset);
            Assert.AreEqual("Graphite", backup.ThemePreset);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LayoutStoreRecoversFromSemanticallyInvalidCurrentProfile()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new LayoutProfileStore(directory);
            var first = Profile("coding", 25);
            var second = Profile("coding", 500);
            await store.SaveAsync(first);
            await store.SaveAsync(second);
            var path = Path.Combine(directory, "coding.json");
            await File.WriteAllTextAsync(
                path,
                """{"Version":1,"Name":"coding","SavedAtUtc":"2026-01-01T00:00:00+00:00","Monitors":null,"Windows":[]}""");

            var recovered = await store.LoadAsync("coding");

            Assert.IsNotNull(recovered);
            Assert.AreEqual(first.Windows[0].Bounds, recovered.Windows[0].Bounds);
            var repaired = System.Text.Json.JsonSerializer.Deserialize<LayoutProfile>(
                await File.ReadAllTextAsync(path));
            var preservedBackup = System.Text.Json.JsonSerializer.Deserialize<LayoutProfile>(
                await File.ReadAllTextAsync(path + ".bak"));
            Assert.IsNotNull(repaired);
            Assert.IsNotNull(preservedBackup);
            Assert.AreEqual(first.Windows[0].Bounds, repaired.Windows[0].Bounds);
            Assert.AreEqual(first.Windows[0].Bounds, preservedBackup.Windows[0].Bounds);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LayoutProfileRoundTripsWindowPlacement()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new LayoutProfileStore(directory);
            var profile = new LayoutProfile(
                LayoutProfile.CurrentVersion,
                "coding",
                DateTimeOffset.UtcNow,
                [new MonitorSnapshot("DISPLAY1", new RectI(0, 0, 1920, 1080), new RectI(0, 0, 1920, 1040))],
                [new WindowPlacementRecord("editor", "EditorClass", 0, new RectI(25, 30, 900, 700), "DISPLAY1", false)]);

            await store.SaveAsync(profile);
            var loaded = await store.LoadAsync("coding");

            Assert.IsNotNull(loaded);
            Assert.AreEqual(profile.Name, loaded.Name);
            Assert.AreEqual(profile.Windows[0], loaded.Windows[0]);
            Assert.AreEqual(profile.Monitors[0], loaded.Monitors[0]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task TransformedLayoutNameRoundTripsCaseInsensitively()
    {
        var directory = CreateDirectory();
        try
        {
            const string name = "work:focus";
            var store = new LayoutProfileStore(directory);
            var profile = Profile(name, 25);

            await store.SaveAsync(profile);

            var safeName = LayoutProfileStore.SafeFileName(name);
            Assert.IsTrue(File.Exists(Path.Combine(directory, safeName + ".json")));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "work_focus.json")));
            var loaded = await store.LoadAsync("WORK:FOCUS");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(name, loaded.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task LayoutStoreRejectsTraversalNames()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new LayoutProfileStore(directory);
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => store.LoadAsync(".."));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("CON"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("CONIN$"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("COM\u00B2.txt"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("LPT\u00B9"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("CON .txt"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("name."));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync(" name"));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.LoadAsync("bad\nname"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void SafeLayoutFileNamesDoNotCollideAfterSanitization()
    {
        var first = LayoutProfileStore.SafeFileName("work:focus");
        var second = LayoutProfileStore.SafeFileName("work?focus");

        Assert.IsTrue(first.StartsWith('!'));
        Assert.AreNotEqual(first, second);
        Assert.IsTrue(string.Equals(
            first,
            LayoutProfileStore.SafeFileName("WORK:FOCUS"),
            StringComparison.OrdinalIgnoreCase));
        Assert.AreNotEqual(first, LayoutProfileStore.SafeFileName(first));
        Assert.AreNotEqual("!focus", LayoutProfileStore.SafeFileName("!focus"));
        Assert.AreEqual("focus", LayoutProfileStore.SafeFileName("focus"));
        Assert.IsFalse(first.Any(Path.GetInvalidFileNameChars().Contains));
        Assert.IsFalse(second.Any(Path.GetInvalidFileNameChars().Contains));
    }

    [TestMethod]
    public async Task LayoutStoreRejectsAmbiguousOrOutOfMonitorData()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new LayoutProfileStore(directory);
            var valid = Profile("ambiguous", 25);
            var duplicate = valid with { Windows = [valid.Windows[0], valid.Windows[0]] };
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(duplicate));
            var controlIdentity = valid with
            {
                Windows = [valid.Windows[0] with { ClassName = "bad\u001fclass" }]
            };
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(controlIdentity));

            var outside = Profile("outside", 25) with
            {
                Monitors =
                [
                    new MonitorSnapshot(
                        "DISPLAY1",
                        new RectI(0, 0, 1920, 1080),
                        new RectI(-1, 0, 1920, 1040))
                ]
            };
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => store.SaveAsync(outside));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
    [TestMethod]
    public async Task AtomicFileRejectsOversizedPrimaryBeforeDeserialization()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var file = new AtomicJsonFile<ThalamusSettings>(path);
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Graphite" });
            await file.SaveAsync(new ThalamusSettings { ThemePreset = "Frost" });
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.SetLength((32L * 1024 * 1024) + 1);

            var recovered = await file.LoadAsync();

            Assert.IsNotNull(recovered);
            Assert.AreEqual("Graphite", recovered.ThemePreset);
            Assert.IsLessThan(32L * 1024 * 1024, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task OversizedWriteDoesNotReplaceValidCurrentFile()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "bounded.json");
            var file = new AtomicJsonFile<string>(path, maximumFileLength: 64);
            await file.SaveAsync("safe");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => file.SaveAsync(new string('x', 1000)));

            Assert.AreEqual("safe", await file.LoadAsync());
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task AtomicFileSerializesConcurrentWritersAcrossInstances()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var first = new AtomicJsonFile<ThalamusSettings>(path);
            var second = new AtomicJsonFile<ThalamusSettings>(path);
            var writes = Enumerable.Range(0, 32)
                .Select(index => (index % 2 == 0 ? first : second).SaveAsync(
                    new ThalamusSettings { ThemePreset = $"Theme-{index}" }));

            await Task.WhenAll(writes);

            var current = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(path));
            var backup = System.Text.Json.JsonSerializer.Deserialize<ThalamusSettings>(
                await File.ReadAllTextAsync(first.BackupPath));
            Assert.IsNotNull(current);
            Assert.IsNotNull(backup);
            StringAssert.StartsWith(current.ThemePreset, "Theme-", StringComparison.Ordinal);
            StringAssert.StartsWith(backup.ThemePreset, "Theme-", StringComparison.Ordinal);
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task AtomicFileSerializesWritersAcrossDifferentGenericTypes()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "shared.json");
            var strings = new AtomicJsonFile<string>(path);
            var objects = new AtomicJsonFile<object>(path);
            var writes = Enumerable.Range(0, 64)
                .Select(index => index % 2 == 0
                    ? strings.SaveAsync($"string-{index}")
                    : objects.SaveAsync((object)$"object-{index}"));

            await Task.WhenAll(writes);

            var current = System.Text.Json.JsonSerializer.Deserialize<string>(
                await File.ReadAllTextAsync(path));
            var backup = System.Text.Json.JsonSerializer.Deserialize<string>(
                await File.ReadAllTextAsync(strings.BackupPath));
            Assert.IsNotNull(current);
            Assert.IsNotNull(backup);
            Assert.IsTrue(
                current.StartsWith("string-", StringComparison.Ordinal) ||
                current.StartsWith("object-", StringComparison.Ordinal));
            Assert.IsTrue(
                backup.StartsWith("string-", StringComparison.Ordinal) ||
                backup.StartsWith("object-", StringComparison.Ordinal));
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task ExternalFileLockHonorsCancellationAndRecovers()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "shared.json");
            var lockPath = path + ".lock";
            var file = new AtomicJsonFile<string>(path);
            using var blocker = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(150));

            var canceled = false;
            try
            {
                await file.SaveAsync("blocked", cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert.IsTrue(canceled);

            blocker.Dispose();
            await file.SaveAsync("recovered");

            Assert.AreEqual("recovered", await file.LoadAsync());
            Assert.IsFalse(File.Exists(lockPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }



    private static LayoutProfile Profile(string name, int x) =>
        new(
            LayoutProfile.CurrentVersion,
            name,
            DateTimeOffset.UtcNow,
            [new MonitorSnapshot("DISPLAY1", new RectI(0, 0, 1920, 1080), new RectI(0, 0, 1920, 1040))],
            [new WindowPlacementRecord("editor", "EditorClass", 0, new RectI(x, 30, 900, 700), "DISPLAY1", false)]);
    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Thalamus.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
