using System.IO;
using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class FunscriptParserTests
{
    private readonly FunscriptParser _sut = new();

    // --- Parse: valid single-axis ---

    [Fact]
    public void Parse_ValidJson_ReturnsCorrectActions()
    {
        var json = """
        {
            "actions": [
                { "at": 1000, "pos": 0 },
                { "at": 2000, "pos": 100 },
                { "at": 3000, "pos": 50 }
            ]
        }
        """;

        var result = _sut.Parse(json, "L0");

        Assert.Equal("L0", result.AxisId);
        Assert.Equal(3, result.Actions.Count);
        Assert.Equal(1000, result.Actions[0].AtMs);
        Assert.Equal(0, result.Actions[0].Pos);
        Assert.Equal(2000, result.Actions[1].AtMs);
        Assert.Equal(100, result.Actions[1].Pos);
        Assert.Equal(3000, result.Actions[2].AtMs);
        Assert.Equal(50, result.Actions[2].Pos);
    }

    [Fact]
    public void Parse_UsesProvidedAxisId()
    {
        var json = """{ "actions": [{ "at": 0, "pos": 50 }] }""";

        var result = _sut.Parse(json, "R1");

        Assert.Equal("R1", result.AxisId);
    }

    [Fact]
    public void Parse_DefaultAxisId_IsL0()
    {
        var json = """{ "actions": [{ "at": 0, "pos": 50 }] }""";

        var result = _sut.Parse(json);

        Assert.Equal("L0", result.AxisId);
    }

    // --- Parse: sorting ---

    [Fact]
    public void Parse_UnsortedActions_SortsByAtMsAscending()
    {
        var json = """
        {
            "actions": [
                { "at": 3000, "pos": 30 },
                { "at": 1000, "pos": 10 },
                { "at": 2000, "pos": 20 }
            ]
        }
        """;

        var result = _sut.Parse(json);

        Assert.Equal(1000, result.Actions[0].AtMs);
        Assert.Equal(2000, result.Actions[1].AtMs);
        Assert.Equal(3000, result.Actions[2].AtMs);
    }

    // --- Parse: pos clamping ---

    [Fact]
    public void Parse_PosAbove100_ClampedTo100()
    {
        var json = """{ "actions": [{ "at": 0, "pos": 150 }] }""";

        var result = _sut.Parse(json);

        Assert.Equal(100, result.Actions[0].Pos);
    }

    [Fact]
    public void Parse_PosBelow0_ClampedTo0()
    {
        var json = """{ "actions": [{ "at": 0, "pos": -20 }] }""";

        var result = _sut.Parse(json);

        Assert.Equal(0, result.Actions[0].Pos);
    }

    // --- Parse: missing/empty actions ---

    [Fact]
    public void Parse_NoActionsProperty_ReturnsEmptyData()
    {
        var json = """{ "metadata": { "title": "test" } }""";

        var result = _sut.Parse(json);

        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Parse_EmptyActionsArray_ReturnsEmptyData()
    {
        var json = """{ "actions": [] }""";

        var result = _sut.Parse(json);

        Assert.Empty(result.Actions);
    }

    // --- Parse: malformed JSON ---

    [Fact]
    public void Parse_MalformedJson_ReturnsEmptyData()
    {
        var result = _sut.Parse("not valid json at all {{{");

        Assert.Equal("L0", result.AxisId);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyData()
    {
        var result = _sut.Parse("");

        Assert.Empty(result.Actions);
    }

    // --- Parse: actions with missing fields ---

    [Fact]
    public void Parse_ActionMissingPos_SkipsAction()
    {
        var json = """
        {
            "actions": [
                { "at": 1000 },
                { "at": 2000, "pos": 50 }
            ]
        }
        """;

        var result = _sut.Parse(json);

        Assert.Single(result.Actions);
        Assert.Equal(2000, result.Actions[0].AtMs);
    }

    [Fact]
    public void Parse_ActionMissingAt_SkipsAction()
    {
        var json = """
        {
            "actions": [
                { "pos": 50 },
                { "at": 2000, "pos": 75 }
            ]
        }
        """;

        var result = _sut.Parse(json);

        Assert.Single(result.Actions);
        Assert.Equal(2000, result.Actions[0].AtMs);
    }

    // --- ParseFile ---

    [Fact]
    public void ParseFile_ReadsFileAndParsesCorrectly()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [
                    { "at": 500, "pos": 25 },
                    { "at": 1500, "pos": 75 }
                ]
            }
            """);

            var result = _sut.ParseFile(tempFile, "R0");

            Assert.Equal("R0", result.AxisId);
            Assert.Equal(tempFile, result.FilePath);
            Assert.Equal(2, result.Actions.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: valid multi-axis ---

    [Fact]
    public void TryParseMultiAxis_ValidMultiAxis_ReturnsAllSupportedAxes()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [
                    { "at": 100, "pos": 50 }
                ],
                "axes": [
                    {
                        "id": "R0",
                        "actions": [{ "at": 200, "pos": 60 }]
                    },
                    {
                        "id": "R1",
                        "actions": [{ "at": 300, "pos": 70 }]
                    },
                    {
                        "id": "R2",
                        "actions": [{ "at": 400, "pos": 80 }]
                    }
                ]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            Assert.Equal(4, result!.Count);
            Assert.True(result.ContainsKey("L0"));
            Assert.True(result.ContainsKey("R0"));
            Assert.True(result.ContainsKey("R1"));
            Assert.True(result.ContainsKey("R2"));

            Assert.Single(result["L0"].Actions);
            Assert.Equal(100, result["L0"].Actions[0].AtMs);

            Assert.Single(result["R0"].Actions);
            Assert.Equal(200, result["R0"].Actions[0].AtMs);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void TryParseMultiAxis_SetsFilePath()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [{ "at": 0, "pos": 50 }],
                "axes": [{ "id": "R0", "actions": [{ "at": 0, "pos": 50 }] }]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            Assert.Equal(tempFile, result!["L0"].FilePath);
            Assert.Equal(tempFile, result["R0"].FilePath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: unsupported axes filtered ---

    [Fact]
    public void TryParseMultiAxis_UnsupportedAxes_AreFiltered()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [{ "at": 0, "pos": 50 }],
                "axes": [
                    { "id": "R0", "actions": [{ "at": 0, "pos": 50 }] },
                    { "id": "L1", "actions": [{ "at": 0, "pos": 50 }] },
                    { "id": "L2", "actions": [{ "at": 0, "pos": 50 }] },
                    { "id": "V0", "actions": [{ "at": 0, "pos": 50 }] }
                ]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            // Only L0 (from top-level) and R0 (supported) should remain
            Assert.Equal(2, result!.Count);
            Assert.True(result.ContainsKey("L0"));
            Assert.True(result.ContainsKey("R0"));
            Assert.False(result.ContainsKey("L1"));
            Assert.False(result.ContainsKey("L2"));
            Assert.False(result.ContainsKey("V0"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: no axes array ---

    [Fact]
    public void TryParseMultiAxis_NoAxesArray_ReturnsNull()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [{ "at": 0, "pos": 50 }]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: empty axes ---

    [Fact]
    public void TryParseMultiAxis_EmptyAxesAndNoTopActions_ReturnsNull()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "axes": []
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.Null(result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: pos clamping and sorting ---

    [Fact]
    public void TryParseMultiAxis_ClampsAndSorts()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [
                    { "at": 2000, "pos": 200 },
                    { "at": 1000, "pos": -50 }
                ],
                "axes": []
            }
            """);

            // Has axes array (empty) but top-level actions → L0
            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            var l0 = result!["L0"];
            Assert.Equal(2, l0.Actions.Count);
            // Sorted ascending
            Assert.Equal(1000, l0.Actions[0].AtMs);
            Assert.Equal(2000, l0.Actions[1].AtMs);
            // Clamped
            Assert.Equal(0, l0.Actions[0].Pos);
            Assert.Equal(100, l0.Actions[1].Pos);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: axis missing id ---

    [Fact]
    public void TryParseMultiAxis_AxisMissingId_IsSkipped()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [{ "at": 0, "pos": 50 }],
                "axes": [
                    { "actions": [{ "at": 0, "pos": 50 }] }
                ]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            Assert.Single(result!); // Only L0
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // --- TryParseMultiAxis: axis with empty actions ---

    [Fact]
    public void TryParseMultiAxis_AxisWithEmptyActions_IsExcluded()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
            {
                "actions": [{ "at": 0, "pos": 50 }],
                "axes": [
                    { "id": "R0", "actions": [] }
                ]
            }
            """);

            var result = _sut.TryParseMultiAxis(tempFile);

            Assert.NotNull(result);
            Assert.Single(result!); // Only L0, R0 has no actions
            Assert.False(result.ContainsKey("R0"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
