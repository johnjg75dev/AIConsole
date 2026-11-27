using AIConsole.Data;

namespace AIConsole.Tests;

public class DbTests
{
    [Fact]
    public void InTransaction_CommitsOnSuccess()
    {
        using var db = new Db(":memory:");
        db.Exec("CREATE TABLE t (v TEXT NOT NULL)");

        db.InTransaction(() =>
        {
            db.Run("INSERT INTO t(v) VALUES(@v)", ("@v", "a"));
            db.Run("INSERT INTO t(v) VALUES(@v)", ("@v", "b"));
        });

        Assert.Equal(2L, Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM t")));
    }

    [Fact]
    public void InTransaction_RollsBackOnException()
    {
        using var db = new Db(":memory:");
        db.Exec("CREATE TABLE t (v TEXT NOT NULL)");

        Assert.Throws<InvalidOperationException>(() => db.InTransaction(() =>
        {
            db.Run("INSERT INTO t(v) VALUES(@v)", ("@v", "a"));
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(0L, Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM t")));
    }

    [Fact]
    public void Constructor_MigratesSchema()
    {
        using var db = new Db(":memory:");

        var tables = db.Query("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name", r => r.GetString(0));

        Assert.Contains("settings", tables);
        Assert.Contains("endpoints", tables);
        Assert.Contains("models", tables);
        Assert.Contains("conversations", tables);
        Assert.Contains("messages", tables);
        Assert.Contains("message_attachments", tables);
    }
}
