using DbaTool.Api.Abstractions;

namespace DbaTool.Api.Services;

public sealed class DbaPathResolver : IDbaPathProvider
{
    public string DataDir { get; }
    public string BackupsDir => Path.Combine(DataDir, "backups");
    public string ConfigsDir => Path.Combine(DataDir, "configs");
    public string TempDir => Path.Combine(DataDir, "tmp");
    public string StateDir => Path.Combine(DataDir, "state");
    public string ConfigFilePath => Path.Combine(ConfigsDir, "db-backup.yml");
    public string SqliteDbPath => Path.Combine(DataDir, "dbatool.db");

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _env;

    public DbaPathResolver(IConfiguration configuration, IHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;
        DataDir = ResolveDataDir(configuration);
        EnsureDirectories();
    }

    public static string ResolveDataDir(IConfiguration configuration)
    {
        var raw = Environment.GetEnvironmentVariable("DBA_DATA_DIR")
                  ?? configuration["DbaTool:DataDir"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
        }

        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, raw));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(ConfigsDir);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(StateDir);
    }

    public string ResolveBinaryPath()
    {
        var binaryName = OperatingSystem.IsWindows() ? "dbb.exe" : "dbb";
        var candidates = new List<string>();

        var configured = Environment.GetEnvironmentVariable("DBA_BINARY_PATH")
                         ?? _configuration["DbaTool:BinaryPath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)));
        }

        // 1. Bundled inside publish/build output directory: <BaseDirectory>/bin/dbb
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "bin", binaryName));
        // 2. Direct in BaseDirectory: <BaseDirectory>/dbb
        candidates.Add(Path.Combine(AppContext.BaseDirectory, binaryName));
        // 3. Relative to ContentRootPath: <ContentRootPath>/bin/dbb
        candidates.Add(Path.Combine(_env.ContentRootPath, "bin", binaryName));
        // 4. Repo root sibling: <ContentRootPath>/../bin/dbb
        candidates.Add(Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "bin", binaryName)));

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                EnsureExecutablePermission(candidate);
                return candidate;
            }
        }

        return binaryName;
    }

    private static void EnsureExecutablePermission(string filePath)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var mode = File.GetUnixFileMode(filePath);
            var execBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            if ((mode & UnixFileMode.UserExecute) == 0)
            {
                File.SetUnixFileMode(filePath, mode | execBits);
            }
        }
        catch
        {
            // Ignore permission changes if file is owned by root/read-only filesystem
        }
    }
}
