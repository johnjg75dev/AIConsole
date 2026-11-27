using AIConsole.Data;
using AIConsole.Models;

namespace AIConsole.Tests;

public class SettingsPersistenceTests
{
    [Fact]
    public void PanelOpacity_DefaultsToOne()
    {
        Assert.Equal(1.0, new AppSettings().PanelOpacity);
    }

    [Fact]
    public void PanelOpacity_RoundTripsThroughSettingsRepo()
    {
        using var db = new Db(":memory:");
        var repo = new SettingsRepo(db);

        repo.Set(SettingKeys.Settings, new AppSettings { PanelOpacity = 0.55 });

        Assert.Equal(0.55, repo.Get(SettingKeys.Settings, new AppSettings()).PanelOpacity);
    }
}
