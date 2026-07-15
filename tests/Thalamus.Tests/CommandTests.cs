using Thalamus.Core.Commands;

namespace Thalamus.Tests;

[TestClass]
public sealed class CommandTests
{
    [TestMethod]
    public void NoArgumentsOpensOverview()
    {
        var result = CommandParser.Parse([]);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(AppCommandKind.ShowOverview, result.Command!.Kind);
    }

    [TestMethod]
    public void ExitCommandNeedsNoArgument()
    {
        var result = CommandParser.Parse(["--exit"]);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(AppCommandKind.Exit, result.Command!.Kind);
        CollectionAssert.AreEqual(new[] { "--exit" }, CommandParser.ToArguments(result.Command));
    }


    [TestMethod]
    [DataRow("left", "left")]
    [DataRow("right", "right")]
    [DataRow("left-third", "left-third")]
    [DataRow("bottom-right", "bottom-right")]
    [DataRow("maximize", "maximize")]
    [DataRow("restore", "restore")]
    public void TileCommandsRoundTrip(string input, string expected)
    {
        var parsed = CommandParser.Parse(["--tile-active", input]);

        Assert.IsTrue(parsed.Success, parsed.Error);
        Assert.AreEqual(expected, parsed.Command!.Argument);
        CollectionAssert.AreEqual(
            new[] { "--tile-active", expected },
            CommandParser.ToArguments(parsed.Command));
    }

    [TestMethod]
    public void RejectsUnknownOrIncompleteCommands()
    {
        Assert.IsFalse(CommandParser.Parse(["--unknown"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--save-layout"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--workspace", "sideways"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--save-layout", ".."]).Success);
        Assert.IsFalse(CommandParser.Parse(["--save-layout", "CON"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--save-layout", "name."]).Success);
        Assert.IsFalse(CommandParser.Parse(["--restore-layout", new string('a', 81)]).Success);
        Assert.IsTrue(CommandParser.Parse(["--save-layout", "work:focus"]).Success);
    }

    [TestMethod]
    public void RejectsNumericEnumArguments()
    {
        Assert.IsFalse(CommandParser.Parse(["--tile-active", "0"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--tile-active", "999"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--workspace", "0"]).Success);
    }

    [TestMethod]
    public void RejectsAmbiguousOrMalformedEnumArguments()
    {
        Assert.IsFalse(CommandParser.Parse(["--tile-active", "left,right"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--tile-active", "l-e-f-t"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--workspace", "next,previous"]).Success);
        Assert.IsFalse(CommandParser.Parse(["--move-active-workspace", "next,previous"]).Success);
    }

    [TestMethod]
    [DataRow("--save-layout")]
    [DataRow("--restore-layout")]
    public void LayoutCommandsRejectOuterWhitespace(string command)
    {
        Assert.IsFalse(CommandParser.Parse([command, " focus"]).Success);
        Assert.IsFalse(CommandParser.Parse([command, "focus "]).Success);
    }

    [TestMethod]
    public void ForwardedEnvelopeRoundTripsWithoutLosingCommand()
    {
        var command = new AppCommand(AppCommandKind.RestoreLayout, "focus");
        var serialized = CommandEnvelope.Create(command).Serialize();

        var restored = CommandEnvelope.Deserialize(serialized);

        Assert.IsNotNull(restored);
        Assert.AreEqual(CommandEnvelope.CurrentVersion, restored.Version);
        Assert.AreEqual(command, restored.Command);
    }

    [TestMethod]
    public void ForwardedEnvelopeRejectsUnknownVersionAndInvalidJson()
    {
        Assert.IsNull(CommandEnvelope.Deserialize("""{"Version":999,"Command":{"Kind":0}}"""));
        Assert.IsNull(CommandEnvelope.Deserialize("{invalid"));
        Assert.IsNull(CommandEnvelope.Deserialize("""{"Version":1,"Command":null}"""));
        Assert.IsNull(CommandEnvelope.Deserialize("""{"Version":1,"Command":{"Kind":999}}"""));
        Assert.IsNull(CommandEnvelope.Deserialize("""{"Version":1,"Command":{"Kind":1,"Argument":"sideways"}}"""));
        Assert.IsNull(CommandEnvelope.Deserialize("""{"Version":1,"Command":{"Kind":4,"Argument":".."}}"""));
        Assert.IsNull(CommandEnvelope.Deserialize(null));
        Assert.IsNull(CommandEnvelope.Deserialize(" "));
    }
}
