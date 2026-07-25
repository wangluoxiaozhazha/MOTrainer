using System.Diagnostics;
using System.Security.Cryptography;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Profiles;

namespace MOTrainer.Core.Session;

public sealed class GameSessionProbe : IGameSessionProbe
{
    private readonly string processName;
    private readonly IReadOnlyList<VersionProfile> profiles;
    private readonly IMemoryAccessorFactory memoryFactory;
    private readonly Dictionary<FileIdentity, string> hashCache = new();
    private readonly object probeGate = new();

    public GameSessionProbe(
        string processName = "gamemd",
        IReadOnlyList<VersionProfile>? profiles = null,
        IMemoryAccessorFactory? memoryFactory = null)
    {
        this.processName = processName;
        this.profiles = profiles ?? KnownProfiles.All;
        this.memoryFactory = memoryFactory ?? ProcessMemoryAccessorFactory.Instance;
    }

    public GameSessionSnapshot Probe()
    {
        lock (probeGate)
        {
            return ProbeCore();
        }
    }

    private GameSessionSnapshot ProbeCore()
    {
        try
        {
            using Process? process = Process
                .GetProcessesByName(processName)
                .OrderBy(candidate => candidate.Id)
                .FirstOrDefault();

            if (process is null)
            {
                return GameSessionSnapshot.NotRunning;
            }

            string? gamePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                return new GameSessionSnapshot(
                    SessionState.Error,
                    process.Id,
                    "无法读取 gamemd.exe 路径");
            }

            string gameHash = GetSha256(gamePath!);
            VersionProfile? profile = profiles.FirstOrDefault(candidate =>
                candidate.Game.Matches(Path.GetFileName(gamePath), gameHash));

            if (profile is null)
            {
                return new GameSessionSnapshot(
                    SessionState.Unsupported,
                    process.Id,
                    $"不受支持的 gamemd.exe：{ShortHash(gameHash)}");
            }

            string? aresPath = FindModulePath(process, profile.Ares.FileName);
            if (aresPath is null)
            {
                return new GameSessionSnapshot(
                    SessionState.ProcessFound,
                    process.Id,
                    "已识别 gamemd.exe，等待 Ares.dll 加载",
                    profile);
            }

            string aresHash = GetSha256(aresPath);
            if (!profile.Ares.Matches(Path.GetFileName(aresPath), aresHash))
            {
                return new GameSessionSnapshot(
                    SessionState.Unsupported,
                    process.Id,
                    $"Ares.dll 版本不受支持：{ShortHash(aresHash)}",
                    profile);
            }

            using IMemoryAccessor memory =
                memoryFactory.Open(process.Id, MemoryAccessMode.ReadOnly);

            if (!memory.TryRead(
                    profile.Selection.CurrentPlayerAddress,
                    out uint currentPlayer) ||
                currentPlayer == 0)
            {
                return new GameSessionSnapshot(
                    SessionState.ModulesReady,
                    process.Id,
                    $"版本已确认：{profile.DisplayName}；等待进入战局",
                    profile);
            }

            if (!memory.TryRead(profile.CurrentFrameAddress, out int _))
            {
                return new GameSessionSnapshot(
                    SessionState.ModulesReady,
                    process.Id,
                    $"版本已确认：{profile.DisplayName}；战局结构尚未就绪",
                    profile);
            }

            return new GameSessionSnapshot(
                SessionState.MatchReady,
                process.Id,
                $"战局已就绪：{profile.DisplayName}（PID {process.Id}）",
                profile);
        }
        catch (Exception exception)
        {
            return new GameSessionSnapshot(
                SessionState.Error,
                null,
                $"进程探测失败：{exception.Message}");
        }
    }

    private string GetSha256(string path)
    {
        FileInfo file = new(path);
        FileIdentity identity = new(
            file.FullName,
            file.Length,
            file.LastWriteTimeUtc.Ticks);

        if (hashCache.TryGetValue(identity, out string? cached))
        {
            return cached;
        }

        using FileStream stream = file.OpenRead();
        byte[] digest;
        using (SHA256 sha256 = SHA256.Create())
        {
            digest = sha256.ComputeHash(stream);
        }

        string hash = BitConverter
            .ToString(digest)
            .Replace("-", string.Empty);
        hashCache[identity] = hash;
        return hash;
    }

    private static string? FindModulePath(Process process, string moduleName)
    {
        foreach (ProcessModule module in process.Modules)
        {
            if (string.Equals(
                    module.ModuleName,
                    moduleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return module.FileName;
            }
        }

        return null;
    }

    private static string ShortHash(string value)
    {
        return value.Length <= 12 ? value : value.Substring(0, 12);
    }

    private readonly record struct FileIdentity(
        string Path,
        long Length,
        long LastWriteTicks);
}
