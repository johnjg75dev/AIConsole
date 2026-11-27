using AIConsole.Data;
using AIConsole.Models;

namespace AIConsole.Tests;

public sealed class EndpointRepoTests : IDisposable
{
    private readonly Db _db;
    private readonly EndpointRepo _repo;

    public EndpointRepoTests()
    {
        _db = new Db(":memory:");
        _repo = new EndpointRepo(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TwoEndpoints_KeepDistinctApiKeys()
    {
        _repo.Upsert(new Endpoint
        {
            Id = "first", Name = "First", BaseUrl = "https://first.test/v1", ApiKey = "sk-first",
        });
        _repo.Upsert(new Endpoint
        {
            Id = "second", Name = "Second", BaseUrl = "https://second.test/v1", ApiKey = "sk-second",
        });

        Assert.Equal("sk-first", _repo.Get("first")?.ApiKey);
        Assert.Equal("sk-second", _repo.Get("second")?.ApiKey);
        Assert.Equal(new[] { "sk-first", "sk-second" }, _repo.All().Select(e => e.ApiKey));
    }

    [Fact]
    public void ApiKeys_AreEncryptedAtRest()
    {
        _repo.Upsert(new Endpoint
        {
            Id = "first", Name = "First", BaseUrl = "https://first.test/v1", ApiKey = "sk-plain",
        });

        var stored = Assert.IsType<string>(_db.Scalar("SELECT api_key_enc FROM endpoints WHERE id='first'"));
        Assert.NotEqual("sk-plain", stored);
        Assert.DoesNotContain("sk-plain", stored);
    }

    [Fact]
    public void UpdatingAnEndpoint_DoesNotEraseAnotherKey()
    {
        _repo.Upsert(new Endpoint { Id = "first", Name = "First", BaseUrl = "https://first.test/v1", ApiKey = "sk-first" });
        _repo.Upsert(new Endpoint { Id = "second", Name = "Second", BaseUrl = "https://second.test/v1", ApiKey = "sk-second" });

        _repo.Upsert(new Endpoint { Id = "first", Name = "First renamed", BaseUrl = "https://first.test/v1", ApiKey = "sk-first-new" });

        Assert.Equal("sk-first-new", _repo.Get("first")?.ApiKey);
        Assert.Equal("sk-second", _repo.Get("second")?.ApiKey);
    }
}
