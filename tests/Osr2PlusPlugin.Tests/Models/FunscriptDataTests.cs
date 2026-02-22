using Osr2PlusPlugin.Models;
using Xunit;

namespace Osr2PlusPlugin.Tests.Models;

public class FunscriptDataTests
{
    [Fact]
    public void FunscriptAction_Record_Equality()
    {
        var a = new FunscriptAction(1000, 50);
        var b = new FunscriptAction(1000, 50);
        Assert.Equal(a, b);
    }

    [Fact]
    public void FunscriptAction_Record_Inequality()
    {
        var a = new FunscriptAction(1000, 50);
        var b = new FunscriptAction(2000, 50);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FunscriptData_Defaults()
    {
        var data = new FunscriptData();
        Assert.Equal("L0", data.AxisId);
        Assert.Equal("", data.FilePath);
        Assert.Empty(data.Actions);
    }

    [Fact]
    public void FunscriptData_ActionsCanBePopulated()
    {
        var data = new FunscriptData
        {
            AxisId = "R0",
            FilePath = "test.funscript",
            Actions = new List<FunscriptAction>
            {
                new(0, 0),
                new(500, 100),
                new(1000, 0)
            }
        };

        Assert.Equal(3, data.Actions.Count);
        Assert.Equal(500, data.Actions[1].AtMs);
        Assert.Equal(100, data.Actions[1].Pos);
    }
}
