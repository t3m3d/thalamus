using Thalamus.Services;

namespace Thalamus.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
    [TestMethod]
    public void RotatesAtConfiguredBoundWithoutAffectingCaller()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Thalamus.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "diagnostics.log");
            var log = new DiagnosticLog(path, maximumBytes: 256);
            for (var index = 0; index < 20; index++)
                log.Write("THA-TEST", $"InvalidOperationException-{index:D2}");

            Assert.IsTrue(log.Flush(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(File.Exists(path + ".1"));
            Assert.IsLessThan(512L, new FileInfo(path).Length);
            Assert.IsLessThan(512L, new FileInfo(path + ".1").Length);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void SanitizesAndBoundsEachRecordToOneLine()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "Thalamus.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "diagnostics.log");
            var log = new DiagnosticLog(path);
            log.Write(
                "THA-TEST\r\nFORGED",
                "detail\tvalue\r\nFORGED-\u2028SECOND\u2029THIRD-" + new string('x', 1000));

            Assert.IsTrue(log.Flush(TimeSpan.FromSeconds(5)));
            var lines = File.ReadAllLines(path);
            Assert.HasCount(1, lines);
            Assert.IsLessThan(400, lines[0].Length);
            StringAssert.Contains(lines[0], "THA-TEST  FORGED", StringComparison.Ordinal);
            Assert.IsFalse(lines[0].Contains("\tvalue", StringComparison.Ordinal));
            Assert.IsFalse(lines[0].Contains('\u2028', StringComparison.Ordinal));
            Assert.IsFalse(lines[0].Contains('\u2029', StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void RejectsNonPositiveLogBound() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new DiagnosticLog(
                Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.log"), maximumBytes: 0));
}
