using System.Text.Json;
using Net.Pi.Ai.Models;

namespace Net.Pi.Core.Sessions;

public class SessionData
{
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string WorkingDirectory { get; set; } = "";
    public string Model { get; set; } = "";
    public List<ChatMessage> Messages { get; set; } = new();
}

public class SessionStore
{
    private readonly string _storageDir;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SessionStore(string? baseDir = null)
    {
        var root = baseDir ?? Directory.GetCurrentDirectory();
        _storageDir = Path.Combine(root, ".net-pi", "sessions");
    }

    public string StorageDirectory => _storageDir;

    public void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    public SessionData CreateSession(string model, string workingDir)
    {
        EnsureDirectoryExists();
        var id = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("n")[..6]}";
        var session = new SessionData
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Model = model,
            WorkingDirectory = workingDir
        };
        SaveSession(session);
        return session;
    }

    public void SaveSession(SessionData session)
    {
        EnsureDirectoryExists();
        session.UpdatedAt = DateTime.UtcNow;
        var filePath = Path.Combine(_storageDir, $"{session.Id}.json");
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public SessionData? LoadSession(string sessionId)
    {
        var filePath = Path.Combine(_storageDir, $"{sessionId}.json");
        if (!File.Exists(filePath))
        {
            // Try prefix matching
            var matches = Directory.GetFiles(_storageDir, $"*{sessionId}*.json");
            if (matches.Length == 1)
            {
                filePath = matches[0];
            }
            else
            {
                return null;
            }
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public List<SessionData> ListSessions(int maxCount = 20)
    {
        if (!Directory.Exists(_storageDir)) return new List<SessionData>();

        var files = Directory.GetFiles(_storageDir, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(maxCount);

        var list = new List<SessionData>();
        foreach (var f in files)
        {
            try
            {
                var json = File.ReadAllText(f);
                var s = JsonSerializer.Deserialize<SessionData>(json, JsonOptions);
                if (s != null) list.Add(s);
            }
            catch { }
        }
        return list;
    }

    public SessionData? GetLatestSession()
    {
        return ListSessions(1).FirstOrDefault();
    }
}
