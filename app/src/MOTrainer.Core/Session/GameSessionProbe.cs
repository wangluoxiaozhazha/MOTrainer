using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using MOTrainer.Core.Localization;
using MOTrainer.Core.Memory;
using MOTrainer.Core.Profiles;

namespace MOTrainer.Core.Session;

public sealed class GameSessionProbe : IGameSessionProbe
{
    private readonly string processName;
    private readonly IReadOnlyList<VersionProfile> profiles;
    private readonly IMemoryAccessorFactory memoryFactory;
    private readonly Func<string, IReadOnlyList<int>> processIdProvider;
    private readonly Func<int, string?> mainModulePathResolver;
    private readonly Func<int, string, string?> modulePathResolver;
    private readonly Func<string, string> hashProvider;
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
        processIdProvider = FindProcessIds;
        mainModulePathResolver = FindMainModulePath;
        modulePathResolver = FindModulePath;
        hashProvider = GetSha256;
    }

    internal GameSessionProbe(
        string processName,
        IReadOnlyList<VersionProfile> profiles,
        IMemoryAccessorFactory memoryFactory,
        Func<string, IReadOnlyList<int>> processIdProvider,
        Func<int, string?> mainModulePathResolver,
        Func<int, string, string?> modulePathResolver,
        Func<string, string> hashProvider)
    {
        this.processName = processName;
        this.profiles = profiles;
        this.memoryFactory = memoryFactory;
        this.processIdProvider = processIdProvider;
        this.mainModulePathResolver = mainModulePathResolver;
        this.modulePathResolver = modulePathResolver;
        this.hashProvider = hashProvider;
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
            IReadOnlyList<int> processIds = processIdProvider(processName);
            if (processIds.Count == 0)
            {
                return GameSessionSnapshot.NotRunning;
            }

            List<GameSessionSnapshot> snapshots = [];
            foreach (int processId in processIds
                .Distinct()
                .OrderBy(candidate => candidate))
            {
                snapshots.Add(ProbeProcess(processId));
            }

            return snapshots
                .OrderByDescending(snapshot => StatePriority(snapshot.State))
                .ThenBy(snapshot => snapshot.ProcessId)
                .First();
        }
        catch (Exception exception)
        {
            return new GameSessionSnapshot(
                SessionState.Error,
                null,
                CoreStrings.Format(
                    "GameProcessEnumerationFailed",
                    exception.Message));
        }
    }

    private GameSessionSnapshot ProbeProcess(int processId)
    {
        string? gamePath;
        try
        {
            gamePath = mainModulePathResolver(processId);
            if (string.IsNullOrWhiteSpace(gamePath))
            {
                return new GameSessionSnapshot(
                    SessionState.Error,
                    processId,
                    CoreStrings.Get("GamePathUnavailable"));
            }
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            return AccessDenied(processId);
        }
        catch (Exception exception)
        {
            return new GameSessionSnapshot(
                SessionState.Error,
                processId,
                CoreStrings.Format(
                    "GamePathReadFailed",
                    exception.Message));
        }

        try
        {
            string gameHash = hashProvider(gamePath!);
            VersionProfile? profile = profiles.FirstOrDefault(candidate =>
                candidate.Game.Matches(Path.GetFileName(gamePath), gameHash));

            if (profile is null)
            {
                return new GameSessionSnapshot(
                    SessionState.Unsupported,
                    processId,
                    CoreStrings.Format(
                        "GameUnsupported",
                        ShortHash(gameHash)));
            }

            string? aresPath;
            try
            {
                aresPath = modulePathResolver(
                    processId,
                    profile.Ares.FileName);
            }
            catch (Exception exception) when (IsAccessDenied(exception))
            {
                return AccessDenied(processId);
            }
            catch (Exception exception)
            {
                return new GameSessionSnapshot(
                    SessionState.Error,
                    processId,
                    CoreStrings.Format(
                        "ModuleListReadFailed",
                        exception.Message),
                    profile);
            }

            if (aresPath is null)
            {
                return new GameSessionSnapshot(
                    SessionState.ProcessFound,
                    processId,
                    CoreStrings.Get("WaitingForAres"),
                    profile);
            }

            string aresHash = hashProvider(aresPath);
            if (!profile.Ares.Matches(Path.GetFileName(aresPath), aresHash))
            {
                return new GameSessionSnapshot(
                    SessionState.Unsupported,
                    processId,
                    CoreStrings.Format(
                        "AresUnsupported",
                        ShortHash(aresHash)),
                    profile);
            }

            using IMemoryAccessor memory =
                memoryFactory.Open(processId, MemoryAccessMode.ReadOnly);

            if (!memory.TryRead(
                    profile.Selection.CurrentPlayerAddress,
                    out uint currentPlayer) ||
                currentPlayer == 0)
            {
                return new GameSessionSnapshot(
                    SessionState.ModulesReady,
                    processId,
                    CoreStrings.Format(
                        "VersionVerifiedWaitingForMatch",
                        ProfileDisplayName(profile)),
                    profile);
            }

            if (!memory.TryRead(profile.CurrentFrameAddress, out int _))
            {
                return new GameSessionSnapshot(
                    SessionState.ModulesReady,
                    processId,
                    CoreStrings.Format(
                        "VersionVerifiedStructuresNotReady",
                        ProfileDisplayName(profile)),
                    profile);
            }

            return new GameSessionSnapshot(
                SessionState.MatchReady,
                processId,
                CoreStrings.Format(
                    "MatchReady",
                    ProfileDisplayName(profile),
                    processId),
                profile);
        }
        catch (Exception exception) when (IsAccessDenied(exception))
        {
            return AccessDenied(processId);
        }
        catch (Exception exception)
        {
            return new GameSessionSnapshot(
                SessionState.Error,
                processId,
                CoreStrings.Format(
                    "ProbeFailed",
                    processId,
                    exception.Message));
        }
    }

    private static int StatePriority(SessionState state) => state switch
    {
        SessionState.MatchReady => 6,
        SessionState.ModulesReady => 5,
        SessionState.VersionVerified => 4,
        SessionState.ProcessFound => 3,
        SessionState.Unsupported => 2,
        SessionState.Error => 1,
        _ => 0,
    };

    private static bool IsAccessDenied(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is Win32Exception win32 && win32.NativeErrorCode == 5;

    private static GameSessionSnapshot AccessDenied(int processId) =>
        new(
            SessionState.Error,
            processId,
            CoreStrings.Format("AccessDenied", processId));

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

    private static IReadOnlyList<int> FindProcessIds(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string? FindMainModulePath(int processId)
    {
        using Process process = Process.GetProcessById(processId);
        return process.MainModule?.FileName;
    }

    private static string? FindModulePath(
        int processId,
        string moduleName)
    {
        using Process process = Process.GetProcessById(processId);
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

    private static string ProfileDisplayName(VersionProfile profile) =>
        profile.Id == KnownProfiles.ObservedMentalOmegaId
            ? CoreStrings.Get("ObservedMentalOmegaProfile")
            : profile.DisplayName;

    private readonly record struct FileIdentity(
        string Path,
        long Length,
        long LastWriteTicks);
}
