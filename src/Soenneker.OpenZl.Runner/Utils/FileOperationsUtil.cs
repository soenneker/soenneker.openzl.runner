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
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.OpenZl.Runner.Utils.Abstract;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Runtime;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.OpenZl.Runner.Utils;

public sealed class FileOperationsUtil(
    IProcessUtil process,
    IDirectoryUtil directoryUtil,
    IFileUtil fileUtil,
    IConfiguration configuration,
    ILogger<FileOperationsUtil> logger,
    IRunnersManager runnersManager) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        bool windows = RuntimeUtil.IsWindows();
        if ((!windows && !RuntimeUtil.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("The OpenZL packages currently target Windows x64 and Linux x64.");
        string rid = windows ? "win-x64" : "linux-x64";
        string library = windows ? "Soenneker.OpenZl.Windows" : "Soenneker.OpenZl.Linux";
        string work = Path.GetFullPath(configuration["OpenZl:WorkingDirectory"] ??
                                       Path.Combine(Path.GetTempPath(),
                                           "openzl-build-" + Guid.NewGuid().ToString("N")));
        await directoryUtil.Create(work, cancellationToken: cancellationToken);
        string source = Path.GetFullPath(configuration["OpenZl:SourceDirectory"] ?? Path.Combine(work, "source"));
        string build = Path.GetFullPath(configuration["OpenZl:BuildDirectory"] ?? Path.Combine(work, "build"));
        string revision = configuration["OpenZl:Revision"] ?? Constants.Revision;
        string cmake = configuration["OpenZl:CMakePath"] ?? "cmake";
        int jobs = configuration.GetValue("OpenZl:Parallelism", Math.Min(Environment.ProcessorCount, 8));
        if (jobs < 1)
            throw new ArgumentOutOfRangeException("OpenZl:Parallelism");
        if (!await directoryUtil.Exists(source, cancellationToken))
        {
            await Run("git", work, ["clone", "--filter=blob:none", "--no-checkout", Constants.Repository, source],
                cancellationToken);
            await Run("git", source, ["checkout", "--detach", revision], cancellationToken);
        }

        List<string> commits = await Run("git", source, ["rev-parse", "HEAD"], cancellationToken);
        List<string> expected = await Run("git", source, ["rev-parse", revision + "^{commit}"], cancellationToken);
        string commit = commits.Single(x => x.Trim().Length == 40).Trim();
        if (expected.All(x => x.Trim() != commit))
            throw new InvalidOperationException("Existing source directory does not match the requested revision.");
        List<string> trackedChanges = await Run("git", source, ["status", "--porcelain", "--untracked-files=no"],
            cancellationToken);
        if (trackedChanges.Any(x => !string.IsNullOrWhiteSpace(x)))
            throw new InvalidOperationException("Pinned native source has tracked modifications.");
        string versionHeader = await fileUtil.Read(Path.Combine(source, "include", "openzl", "zl_version.h"), cancellationToken: cancellationToken);

        string Component(string name)
        {
            Match match = Regex.Match(versionHeader, @"#define\s+ZL_LIBRARY_VERSION_" + name + @"\s+(\d+)");
            if (!match.Success)
                throw new InvalidDataException("Native version header is invalid.");
            return match.Groups[1].Value;
        }

        var version = $"{Component("MAJOR")}.{Component("MINOR")}.{Component("PATCH")}";
        var configure = new List<string>
        {
            "-S", source, "-B", build, "-DCMAKE_BUILD_TYPE=Release", "-DOPENZL_BUILD_TESTS=OFF",
            "-DOPENZL_BUILD_EXAMPLES=OFF", "-DOPENZL_BUILD_SHARED_LIBS=ON", "-DBUILD_SHARED_LIBS=ON",
            "-DZSTD_BUILD_SHARED=OFF", "-DOPENZL_BUILD_CLI=OFF",
            "-DOPENZL_BUILD_CPP=OFF", "-DOPENZL_BUILD_TOOLS=OFF", "-DOPENZL_BUILD_CUSTOM_PARSERS=OFF",
            "-DOPENZL_CPP_INSTALL=OFF",
            "-DCMAKE_POSITION_INDEPENDENT_CODE=ON", "-DCMAKE_WINDOWS_EXPORT_ALL_SYMBOLS=ON"
        };
        string? generator = configuration["OpenZl:CMakeGenerator"] ?? (windows ? "Ninja" : null);
        if (windows)
        {
            configure.Add("-DCMAKE_C_COMPILER=" + (configuration["OpenZl:CCompiler"] ?? "clang-cl"));
            configure.Add("-DCMAKE_CXX_COMPILER=" + (configuration["OpenZl:CxxCompiler"] ?? "clang-cl"));
        }

        if (!string.IsNullOrWhiteSpace(generator))
        {
            configure.Add("-G");
            configure.Add(generator);
        }

        string? toolset = configuration["OpenZl:CMakeToolset"];
        if (!string.IsNullOrWhiteSpace(toolset))
        {
            configure.Add("-T");
            configure.Add(toolset);
        }

        if (windows && (generator == null || generator.StartsWith("Visual Studio", StringComparison.Ordinal)))
        {
            configure.Add("-A");
            configure.Add("x64");
        }

        await Run(cmake, work, configure, cancellationToken);
        await Run(cmake, work,
            ["--build", build, "--config", "Release", "--target", "openzl", "--parallel", jobs.ToString()],
            cancellationToken);
        string executableName = windows ? "openzl.dll" : "libopenzl.so";
        string executable = (await directoryUtil.GetFilesByExtension(build, Path.GetExtension(executableName), recursive: true, cancellationToken: cancellationToken))
            .Single(path => Path.GetFileName(path) == executableName);
        string stage =
            Path.GetFullPath(configuration["OpenZl:OutputDirectory"] ?? Path.Combine(work, "stage", rid, "native"));
        await directoryUtil.Create(stage, cancellationToken: cancellationToken);
        await fileUtil.Copy(executable, Path.Combine(stage, executableName), cancellationToken: cancellationToken);
        if (!windows)
            File.SetUnixFileMode(Path.Combine(stage, executableName),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string licenses = Path.Combine(stage, "licenses");
        await directoryUtil.Create(licenses, cancellationToken: cancellationToken);
        await CopyLicense(source, "LICENSE", licenses, "OpenZL.txt", cancellationToken);
        await CopyLicense(Path.Combine(source, "deps", "zstd"), "LICENSE", licenses, "Zstandard.txt", cancellationToken);
        await CopyLicense(Path.Combine(source, "deps", "lz4", "lib"), "LICENSE", licenses, "LZ4.txt", cancellationToken);
        string exe = Path.Combine(stage, executableName);
        NativeSmoke.Verify(exe);
        string sha = Convert.ToHexStringLower(SHA256.HashData(await fileUtil.ReadToBytes(exe, cancellationToken: cancellationToken)));
        await fileUtil.Write(Path.Combine(stage, "SOURCE.txt"),
            $"Upstream: {Constants.Repository}\nCommit: {commit}\nRuntime: {rid}\nVersion: {version}\nSHA256 ({executableName}): {sha}\n",
            cancellationToken: cancellationToken);
        logger.LogInformation("Built and verified OpenZL {Runtime} at {Stage}", rid, stage);
        if (configuration.GetValue<bool>("OpenZl:UpdateRepository"))
            await runnersManager.PushIfChangesNeededForDirectory(
                Path.Combine(rid, "native"), stage, library,
                $"https://github.com/soenneker/{library.ToLowerInvariantFast()}", false, cancellationToken, commit[..12]);
    }

    private ValueTask<List<string>> Run(string executable, string working, IEnumerable<string> arguments,
        CancellationToken token) =>
        process.Start(executable, working, ProcessArguments.Join(arguments), timeout: TimeSpan.FromHours(2), log: true,
            cancellationToken: token);

    private async ValueTask CopyLicense(string source, string name, string destination, string outputName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(source, name);
        if (!await fileUtil.Exists(path, cancellationToken))
            throw new FileNotFoundException("Required third-party license missing.", path);
        await fileUtil.Copy(path, Path.Combine(destination, outputName), cancellationToken: cancellationToken);
    }
}