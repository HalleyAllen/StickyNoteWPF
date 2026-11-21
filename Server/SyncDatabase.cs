using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using StickyNoteWPF.Sync;

namespace StickyNote.Sync.Server;

public sealed class SyncDatabase
{
    private readonly string _connectionString;

    public SyncDatabase(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var directory = Path.GetFullPath(configuration["Sync:DataDirectory"] ?? "data", environment.ContentRootPath);
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "sync.db"), ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 15
        }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY, Username TEXT NOT NULL, NormalizedName TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Sessions (
                TokenHash TEXT PRIMARY KEY, UserId TEXT NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                ExpiresAt INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS Snapshots (
                UserId TEXT PRIMARY KEY REFERENCES Users(Id) ON DELETE CASCADE,
                Revision INTEGER NOT NULL DEFAULT 0, Payload TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    public record User(string Id, string Username, string PasswordHash);

    public User? FindUser(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash FROM Users WHERE NormalizedName=$name";
        cmd.Parameters.AddWithValue("$name", username.ToUpperInvariant());
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public bool CreateUser(User user)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Users VALUES($id,$name,$normalized,$hash);
            INSERT INTO Snapshots VALUES($id,0,'{"notes":[],"taskLists":[]}');
            """;
        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.Parameters.AddWithValue("$name", user.Username);
        cmd.Parameters.AddWithValue("$normalized", user.Username.ToUpperInvariant());
        cmd.Parameters.AddWithValue("$hash", user.PasswordHash);
        try { cmd.ExecuteNonQuery(); transaction.Commit(); return true; }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    }

    public LoginResult CreateSession(User user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Sessions WHERE ExpiresAt <= $now;
            INSERT INTO Sessions VALUES($hash,$user,$expires);
            """;
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$hash", TokenHash(token));
        cmd.Parameters.AddWithValue("$user", user.Id);
        cmd.Parameters.AddWithValue("$expires", expires.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
        return new(user.Id, user.Username, token, expires);
    }

    public string? Authenticate(string token)
    {
        if (token.Length != 64) return null;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT UserId FROM Sessions WHERE TokenHash=$hash AND ExpiresAt>$now";
        cmd.Parameters.AddWithValue("$hash", TokenHash(token));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return cmd.ExecuteScalar() as string;
    }

    public void Revoke(string token)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM Sessions WHERE TokenHash=$hash";
        cmd.Parameters.AddWithValue("$hash", TokenHash(token));
        cmd.ExecuteNonQuery();
    }

    public RemoteSnapshot Read(string userId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Revision,Payload FROM Snapshots WHERE UserId=$id";
        cmd.Parameters.AddWithValue("$id", userId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Account snapshot missing.");
        return new(reader.GetInt64(0), System.Text.Json.JsonSerializer.Deserialize<SyncData>(reader.GetString(1), SnapshotTools.Json)!);
    }

    public bool Write(string userId, UploadSnapshot upload)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        // 单条条件更新：多个设备同时上传时只有一个能成功，绝不静默覆盖。
        cmd.CommandText = "UPDATE Snapshots SET Revision=Revision+1,Payload=$payload WHERE UserId=$id AND Revision=$revision";
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.Parameters.AddWithValue("$revision", upload.ExpectedRevision);
        cmd.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(upload.Data, SnapshotTools.Json));
        return cmd.ExecuteNonQuery() == 1;
    }

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
