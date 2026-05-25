using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using OpenSteamTool.Manager.Models;

namespace OpenSteamTool.Manager.Services;

public sealed class GamePackageInstallService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly GitHubHttpService github;

    public GamePackageInstallService(GitHubHttpService github)
    {
        this.github = github;
    }

    public async Task<GamePackageInstallRecord> InstallAsync(
        string steamPath,
        SteamGameInfo game,
        GitHubGamePackage package,
        string manifestUrl = "",
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamPath);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(package);

        if (string.IsNullOrWhiteSpace(game.AppId))
        {
            throw new InvalidOperationException("Game AppId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(package.ZipUrl))
        {
            throw new InvalidOperationException("Package zip url cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(package.Sha256))
        {
            throw new InvalidOperationException("Package SHA-256 cannot be empty.");
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var tempRoot = Path.Combine(Path.GetTempPath(), "OpenSteamTool.Manager", "game-installs", game.AppId, stamp);
        var downloadDir = Path.Combine(tempRoot, "download");
        var stageDir = Path.Combine(tempRoot, "stage");
        var recordDir = Path.Combine(steamPath, "opensteamtool-manager", "game-installs", game.AppId, stamp);
        var backupDir = Path.Combine(recordDir, "backup");

        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(stageDir);
        Directory.CreateDirectory(backupDir);

        try
        {
            var zipName = GetArchiveName(package);
            var zipPath = Path.Combine(downloadDir, zipName);

            progress?.Report(new UpdateProgress { Stage = "downloading", Percent = 0, Message = $"Downloading {package.Name}..." });
            await DownloadFileWithFallbackAsync(package.ZipUrl, package.OriginalZipUrl, zipPath, progress, cancellationToken);

            var downloadedHash = ComputeSha256(zipPath);
            if (!string.Equals(downloadedHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Package hash mismatch. expected {package.Sha256}, actual {downloadedHash}.");
            }

            progress?.Report(new UpdateProgress { Stage = "extracting", Percent = 0, Message = "Extracting package..." });
            ExtractZipSafely(zipPath, stageDir);

            var installRoot = ResolveInstallRoot(game.InstallPath, package.TargetRelativePath);
            Directory.CreateDirectory(installRoot);

            var record = new GamePackageInstallRecord
            {
                RecordDirectory = recordDir,
                AppId = game.AppId,
                GameName = game.Name,
                GameInstallPath = game.InstallPath,
                PackageName = package.Name,
                PackageDescription = package.Description,
                PackageZipUrl = package.ZipUrl,
                PackageSha256 = package.Sha256,
                TargetRelativePath = package.TargetRelativePath,
                ManifestUrl = manifestUrl,
                InstalledAtUtc = DateTimeOffset.UtcNow
            };

            progress?.Report(new UpdateProgress { Stage = "installing", Percent = 0, Message = "Copying files to game folder..." });
            CopyStagedFiles(stageDir, installRoot, backupDir, record, progress);

            await SaveRecordAsync(record, recordDir, cancellationToken);
            progress?.Report(new UpdateProgress { Stage = "done", Percent = 100, Message = "Package installed." });
            return record;
        }
        catch
        {
            if (Directory.Exists(recordDir))
            {
                try
                {
                    Directory.Delete(recordDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failures.
                }
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public GamePackageInstallRecord? GetLatestInstallRecord(string steamPath, string appId)
    {
        var appRoot = GetAppRecordRoot(steamPath, appId);
        if (!Directory.Exists(appRoot))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(appRoot).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var recordPath = Path.Combine(directory, "install-record.json");
            if (!File.Exists(recordPath))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<GamePackageInstallRecord>(File.ReadAllText(recordPath), JsonOptions);
                if (record is not null)
                {
                    record.RecordDirectory = directory;
                    return record;
                }
            }
            catch
            {
                // Ignore corrupted records and continue searching.
            }
        }

        return null;
    }

    public async Task<GamePackageInstallRecord> RollbackLatestAsync(
        string steamPath,
        SteamGameInfo game,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamPath);
        ArgumentNullException.ThrowIfNull(game);

        var record = GetLatestInstallRecord(steamPath, game.AppId)
            ?? throw new InvalidOperationException("No rollback record exists for this game.");

        foreach (var file in record.Files)
        {
            if (!File.Exists(file.TargetPath))
            {
                continue;
            }

            var currentHash = await Task.Run(() => ComputeSha256(file.TargetPath), cancellationToken);
            if (!string.Equals(currentHash, file.InstalledHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"File was modified by user: {file.RelativePath}");
            }
        }

        foreach (var file in record.Files)
        {
            if (!string.IsNullOrWhiteSpace(file.BackupPath) && File.Exists(file.BackupPath))
            {
                var targetDirectory = Path.GetDirectoryName(file.TargetPath);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(file.BackupPath, file.TargetPath, overwrite: true);
                continue;
            }

            if (File.Exists(file.TargetPath))
            {
                File.Delete(file.TargetPath);
            }
        }

        try
        {
            Directory.Delete(record.RecordDirectory, recursive: true);
        }
        catch
        {
            // Keep the record if cleanup fails.
        }

        return record;
    }

    public string GetTargetDirectory(SteamGameInfo game, GitHubGamePackage package)
        => ResolveInstallRoot(game.InstallPath, package.TargetRelativePath);

    private static string GetAppRecordRoot(string steamPath, string appId)
        => Path.Combine(steamPath, "opensteamtool-manager", "game-installs", appId);

    private static string ResolveInstallRoot(string gameInstallPath, string targetRelativePath)
    {
        if (string.IsNullOrWhiteSpace(targetRelativePath))
        {
            return gameInstallPath;
        }

        return Path.Combine(gameInstallPath, targetRelativePath);
    }

    private static void CopyStagedFiles(
        string stageDir,
        string installRoot,
        string backupRoot,
        GamePackageInstallRecord record,
        IProgress<UpdateProgress>? progress)
    {
        var files = Directory.EnumerateFiles(stageDir, "*", SearchOption.AllDirectories).ToList();
        var total = Math.Max(files.Count, 1);

        for (var index = 0; index < files.Count; index++)
        {
            var sourcePath = files[index];
            var relativePath = Path.GetRelativePath(stageDir, sourcePath);
            var targetPath = Path.Combine(installRoot, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var installedHash = ComputeSha256(sourcePath);
            string? backupPath = null;
            var wasExisting = File.Exists(targetPath);
            if (wasExisting)
            {
                backupPath = Path.Combine(backupRoot, relativePath);
                var backupDirectory = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrWhiteSpace(backupDirectory))
                {
                    Directory.CreateDirectory(backupDirectory);
                }

                File.Copy(targetPath, backupPath, overwrite: true);
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            record.Files.Add(new GamePackageInstalledFile
            {
                RelativePath = relativePath,
                TargetPath = targetPath,
                InstalledHash = installedHash,
                BackupPath = backupPath,
                WasExisting = wasExisting
            });

            progress?.Report(new UpdateProgress
            {
                Stage = "installing",
                Percent = (int)Math.Clamp(((index + 1) * 100L) / total, 0, 100),
                Message = relativePath
            });
        }
    }

    private async Task DownloadFileWithFallbackAsync(
        string primaryUrl,
        string? mirrorUrl,
        string destinationPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(primaryUrl))
        {
            candidates.Add(primaryUrl.Trim());
        }

        if (!string.IsNullOrWhiteSpace(mirrorUrl) && !candidates.Contains(mirrorUrl.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(mirrorUrl.Trim());
        }

        Exception? lastFailure = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var url = candidates[index];
            try
            {
                using var request = github.CreateRequest(HttpMethod.Get, url, "application/octet-stream", includeGitHubHeaders: false);
                using var response = await github.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var message = await github.BuildFailureMessageAsync(response, "Resource package download failed", cancellationToken, includeRateLimitDetails: false);
                    throw new InvalidOperationException(message);
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(destinationPath);

                var totalLength = response.Content.Headers.ContentLength;
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    readTotal += read;

                    if (totalLength is > 0)
                    {
                        var percent = (int)Math.Clamp(readTotal * 100L / totalLength.Value, 0, 100);
                        progress?.Report(new UpdateProgress
                        {
                            Stage = "downloading",
                            Percent = percent,
                            Message = $"{percent}%"
                        });
                    }
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                if (index < candidates.Count - 1)
                {
                    progress?.Report(new UpdateProgress
                    {
                        Stage = "downloading",
                        Percent = 0,
                        Message = "CDN 下载失败，正在尝试备用地址..."
                    });
                }
            }
        }

        throw new InvalidOperationException(lastFailure?.Message ?? "Resource package download failed.");
    }

    private static void ExtractZipSafely(string zipPath, string stageDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var stageRoot = Path.GetFullPath(stageDir);

        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(fullName) || fullName.EndsWith(Path.DirectorySeparatorChar))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(stageRoot, fullName));
            if (!destinationPath.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"ZIP contains invalid path: {entry.FullName}");
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var source = entry.Open();
            using var destination = File.Create(destinationPath);
            source.CopyTo(destination);
        }
    }

    private static async Task SaveRecordAsync(GamePackageInstallRecord record, string recordDir, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(recordDir);
        var recordPath = Path.Combine(recordDir, "install-record.json");
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(recordPath, json, cancellationToken);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string GetArchiveName(GitHubGamePackage package)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(package.ZipUrl).LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? fileName
                    : $"{fileName}.zip";
            }
        }
        catch
        {
            // ignore
        }

        return $"{SanitizeFileName(package.AppId)}-{SanitizeFileName(package.Name)}.zip";
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "package";
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
