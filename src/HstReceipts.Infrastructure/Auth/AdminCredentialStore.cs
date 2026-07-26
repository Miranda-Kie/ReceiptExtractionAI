using System.Collections.Concurrent;
using System.Text.Json;
using HstReceipts.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HstReceipts.Infrastructure.Auth;

/// <summary>
/// Plaintext passwords known to this process for Admin Users "Show".
/// Seed baselines are in-memory; passwords set/reset in-app are also persisted
/// to a gitignored local file so Show still works after a restart.
/// </summary>
public sealed class AdminCredentialStore : IAdminCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, string> _passwords =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _persistPath;
    private readonly ILogger<AdminCredentialStore> _logger;
    private readonly object _fileLock = new();

    public AdminCredentialStore(IHostEnvironment environment, ILogger<AdminCredentialStore> logger)
    {
        _logger = logger;
        if (environment.IsDevelopment())
        {
            _persistPath = Path.Combine(environment.ContentRootPath, "admin-credentials.local.json");
            LoadPersisted();
        }
    }

    public void Remember(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        _passwords[username.Trim()] = password;
        SavePersisted();
    }

    /// <summary>
    /// Seed baseline for Show without overwriting persisted reset passwords on disk.
    /// </summary>
    public void RememberSeedBaseline(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var key = username.Trim();
        // Persisted reset/create passwords win over seed config.
        if (_passwords.ContainsKey(key))
        {
            return;
        }

        _passwords[key] = password;
    }

    public void Forget(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        if (_passwords.TryRemove(username.Trim(), out _))
        {
            SavePersisted();
        }
    }

    public string? TryGet(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return _passwords.TryGetValue(username.Trim(), out var password) ? password : null;
    }

    private void LoadPersisted()
    {
        if (_persistPath is null || !File.Exists(_persistPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistPath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data is null)
            {
                return;
            }

            foreach (var (user, password) in data)
            {
                if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
                {
                    _passwords[user.Trim()] = password;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load {Path} for admin password Show.", _persistPath);
        }
    }

    private void SavePersisted()
    {
        if (_persistPath is null)
        {
            return;
        }

        try
        {
            lock (_fileLock)
            {
                var snapshot = _passwords.ToDictionary(
                    static p => p.Key,
                    static p => p.Value,
                    StringComparer.OrdinalIgnoreCase);
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(_persistPath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save {Path} for admin password Show.", _persistPath);
        }
    }
}
