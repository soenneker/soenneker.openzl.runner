using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.OpenZl.Runner.Utils.Abstract;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.OpenZl.Runner.Utils;

public sealed class FileOperationsUtil(IProcessUtil process, IConfiguration configuration,
    ILogger<FileOperationsUtil> logger, IServiceProvider services) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        bool windows = OperatingSystem.IsWindows();
        if ((!windows && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The OpenZL packages currently target Windows x64 and Linux x64.");
        string rid = windows ? "win-x64" : "linux-x64";
        string library = windows ? "Soenneker.OpenZl.Windows" : "Soenneker.OpenZl.Linux";
        string work = Path.GetFullPath(configuration["OpenZl:WorkingDirectory"] ?? Path.Combine(Path.GetTempPath(), "openzl-build-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(work);
        string source = Path.GetFullPath(configuration["OpenZl:SourceDirectory"] ?? Path.Combine(work, "source"));
        string build = Path.GetFullPath(configuration["OpenZl:BuildDirectory"] ?? Path.Combine(work, "build"));
        string revision = configuration["OpenZl:Revision"] ?? Constants.Revision;
        string cmake = configuration["OpenZl:CMakePath"] ?? "cmake";
        int jobs = configuration.GetValue("OpenZl:Parallelism", Math.Min(Environment.ProcessorCount, 8));
        if (jobs < 1) throw new ArgumentOutOfRangeException("OpenZl:Parallelism");
        if (!Directory.Exists(source))
        {
            await Run("git", work, ["clone", "--filter=blob:none", "--no-checkout", Constants.Repository, source], cancellationToken);
            await Run("git", source, ["checkout", "--detach", revision], cancellationToken);
        }
        var commits = await Run("git", source, ["rev-parse", "HEAD"], cancellationToken);
        var expected = await Run("git", source, ["rev-parse", revision + "^{commit}"], cancellationToken);
        string commit = commits.Single(x => x.Trim().Length == 40).Trim();
        if (!expected.Any(x => x.Trim() == commit)) throw new InvalidOperationException("Existing source directory does not match the requested revision.");
        var trackedChanges = await Run("git", source, ["status", "--porcelain", "--untracked-files=no"], cancellationToken);
        if (trackedChanges.Any(x => !string.IsNullOrWhiteSpace(x))) throw new InvalidOperationException("Pinned native source has tracked modifications.");
        string versionHeader = await File.ReadAllTextAsync(Path.Combine(source, "include", "openzl", "zl_version.h"), cancellationToken);
        string Component(string name)
        {
            var match = Regex.Match(versionHeader, @"#define\s+ZL_LIBRARY_VERSION_" + name + @"\s+(\d+)");
            if (!match.Success) throw new InvalidDataException("Native version header is invalid.");
            return match.Groups[1].Value;
        }
        string version = $"{Component("MAJOR")}.{Component("MINOR")}.{Component("PATCH")}";
        var configure = new List<string> { "-S", source, "-B", build, "-DCMAKE_BUILD_TYPE=Release", "-DOPENZL_BUILD_TESTS=OFF",
            "-DOPENZL_BUILD_EXAMPLES=OFF", "-DOPENZL_BUILD_SHARED_LIBS=ON", "-DBUILD_SHARED_LIBS=ON", "-DZSTD_BUILD_SHARED=OFF", "-DOPENZL_BUILD_CLI=OFF",
            "-DOPENZL_BUILD_CPP=OFF", "-DOPENZL_BUILD_TOOLS=OFF", "-DOPENZL_BUILD_CUSTOM_PARSERS=OFF", "-DOPENZL_CPP_INSTALL=OFF",
            "-DCMAKE_POSITION_INDEPENDENT_CODE=ON", "-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON" };
        string? generator = configuration["OpenZl:CMakeGenerator"] ?? (windows ? "Ninja" : null);
        if (windows) { configure.Add("-DCMAKE_C_COMPILER=" + (configuration["OpenZl:CCompiler"] ?? "clang-cl")); configure.Add("-DCMAKE_CXX_COMPILER=" + (configuration["OpenZl:CxxCompiler"] ?? "clang-cl")); }
        if (!string.IsNullOrWhiteSpace(generator)) { configure.Add("-G"); configure.Add(generator); }
        string? toolset = configuration["OpenZl:CMakeToolset"];
        if (!string.IsNullOrWhiteSpace(toolset)) { configure.Add("-T"); configure.Add(toolset); }
        if (windows && (generator == null || generator.StartsWith("Visual Studio", StringComparison.Ordinal))) { configure.Add("-A"); configure.Add("x64"); }
        await Run(cmake, work, configure, cancellationToken);
        await Run(cmake, work, ["--build", build, "--config", "Release", "--target", "openzl", "--parallel", jobs.ToString()], cancellationToken);
        string executableName = windows ? "openzl.dll" : "libopenzl.so";
        string executable = Directory.EnumerateFiles(build, executableName, SearchOption.AllDirectories).Single();
        string stage = Path.GetFullPath(configuration["OpenZl:OutputDirectory"] ?? Path.Combine(work, "stage", rid, "native"));
        Directory.CreateDirectory(stage);
        File.Copy(executable, Path.Combine(stage, executableName), true);
        if (!windows) File.SetUnixFileMode(Path.Combine(stage, executableName), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string licenses = Path.Combine(stage, "licenses"); Directory.CreateDirectory(licenses);
        CopyLicense(source, "LICENSE", licenses, "OpenZL.txt");
        CopyLicense(Path.Combine(source, "deps", "zstd"), "LICENSE", licenses, "Zstandard.txt");
        CopyLicense(Path.Combine(source, "deps", "lz4", "lib"), "LICENSE", licenses, "LZ4.txt");
        string exe = Path.Combine(stage, executableName);
        NativeSmoke.Verify(exe);
        string sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(exe, cancellationToken)));
        await File.WriteAllTextAsync(Path.Combine(stage, "SOURCE.txt"), $"Upstream: {Constants.Repository}\nCommit: {commit}\nRuntime: {rid}\nVersion: {version}\nSHA256 ({executableName}): {sha}\n", cancellationToken);
        logger.LogInformation("Built and verified OpenZL {Runtime} at {Stage}", rid, stage);
        if (configuration.GetValue<bool>("OpenZl:UpdateRepository"))
            await services.GetRequiredService<IRunnersManager>().PushIfChangesNeededForDirectory(Path.Combine(rid, "native"), stage, library,
                $"https://github.com/soenneker/{library.ToLowerInvariant()}", false, cancellationToken, commit[..12]);
    }
    private ValueTask<List<string>> Run(string executable, string working, IEnumerable<string> arguments, CancellationToken token) =>
        process.Start(executable, working, ProcessArguments.Join(arguments), timeout: TimeSpan.FromHours(2), log: true, cancellationToken: token);
    private static void CopyLicense(string source, string name, string destination, string outputName)
    {
        string path = Path.Combine(source, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Required third-party license missing.", path);
        File.Copy(path, Path.Combine(destination, outputName), true);
    }
}
