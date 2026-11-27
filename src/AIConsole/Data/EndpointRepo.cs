using AIConsole.Models;
using AIConsole.Services;

namespace AIConsole.Data;

public sealed class EndpointRepo
{
    private readonly Db _db;
    public EndpointRepo(Db db) => _db = db;

    private static Endpoint Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        BaseUrl = r.GetString(2),
        ApiKey = Dpapi.Unprotect(r.IsDBNull(3) ? null : r.GetString(3)),
        ShowAllModels = r.GetInt64(4) != 0,
        CreatedAt = DateTimeOffset.Parse(r.GetString(5)),
    };

    public List<Endpoint> All() =>
        _db.Query("SELECT id,name,base_url,api_key_enc,show_all,created_at FROM endpoints ORDER BY created_at", Map);

    public Endpoint? Get(string id) =>
        _db.Query("SELECT id,name,base_url,api_key_enc,show_all,created_at FROM endpoints WHERE id=@id",
            Map, ("@id", id)).FirstOrDefault();

    public void Upsert(Endpoint e)
    {
        _db.Run("""
            INSERT INTO endpoints(id,name,base_url,api_key_enc,show_all,created_at)
            VALUES(@id,@name,@url,@key,@show,@created)
            ON CONFLICT(id) DO UPDATE SET name=@name, base_url=@url, api_key_enc=@key, show_all=@show
            """,
            ("@id", e.Id), ("@name", e.Name), ("@url", e.BaseUrl),
            ("@key", Dpapi.Protect(e.ApiKey)), ("@show", e.ShowAllModels ? 1 : 0),
            ("@created", e.CreatedAt.ToString("o")));
    }

    public void Delete(string id) => _db.Run("DELETE FROM endpoints WHERE id=@id", ("@id", id));

    // ---- models ----

    private static ModelInfo MapModel(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        EndpointId = r.GetString(1),
        ModelId = r.GetString(2),
        Enabled = r.GetInt64(3) != 0,
        LastSeen = DateTimeOffset.Parse(r.GetString(4)),
        EndpointName = r.IsDBNull(5) ? "" : r.GetString(5),
    };

    public List<ModelInfo> ModelsFor(string endpointId) =>
        _db.Query("SELECT m.id,m.endpoint_id,m.model_id,m.enabled,m.last_seen,e.name FROM models m JOIN endpoints e ON e.id=m.endpoint_id WHERE m.endpoint_id=@id ORDER BY m.model_id",
            MapModel, ("@id", endpointId));

    public List<ModelInfo> EnabledModels() =>
        _db.Query("SELECT m.id,m.endpoint_id,m.model_id,m.enabled,m.last_seen,e.name FROM models m JOIN endpoints e ON e.id=m.endpoint_id WHERE m.enabled=1 ORDER BY e.name, m.model_id",
            MapModel);

    public List<ModelInfo> AllModels() =>
        _db.Query("SELECT m.id,m.endpoint_id,m.model_id,m.enabled,m.last_seen,e.name FROM models m JOIN endpoints e ON e.id=m.endpoint_id ORDER BY e.name, m.model_id",
            MapModel);

    /// <summary>Merge a freshly fetched model list. Enabled state is preserved; unseen models are pruned.</summary>
    public void SyncModels(string endpointId, IEnumerable<string> modelIds, bool autoEnable, bool prune)
    {
        var now = DateTimeOffset.Now.ToString("o");
        var ids = modelIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var mid in ids)
        {
            _db.Run("""
                INSERT INTO models(endpoint_id,model_id,enabled,last_seen) VALUES(@e,@m,@en,@t)
                ON CONFLICT(endpoint_id,model_id) DO UPDATE SET last_seen=@t
                """,
                ("@e", endpointId), ("@m", mid), ("@en", autoEnable ? 1 : 0), ("@t", now));
        }
        if (prune && ids.Count > 0)
        {
            // delete rows for this endpoint that were not refreshed just now
            _db.Run("DELETE FROM models WHERE endpoint_id=@e AND last_seen < @t", ("@e", endpointId), ("@t", now));
        }
    }

    public void SetEnabled(long modelRowId, bool enabled) =>
        _db.Run("UPDATE models SET enabled=@en WHERE id=@id", ("@id", modelRowId), ("@en", enabled ? 1 : 0));

    public void SetAllEnabled(string endpointId, bool enabled) =>
        _db.Run("UPDATE models SET enabled=@en WHERE endpoint_id=@e", ("@e", endpointId), ("@en", enabled ? 1 : 0));

    public void AddManualModel(string endpointId, string modelId) =>
        _db.Run("""
            INSERT INTO models(endpoint_id,model_id,enabled,last_seen) VALUES(@e,@m,1,@t)
            ON CONFLICT(endpoint_id,model_id) DO NOTHING
            """, ("@e", endpointId), ("@m", modelId), ("@t", DateTimeOffset.Now.ToString("o")));
}
