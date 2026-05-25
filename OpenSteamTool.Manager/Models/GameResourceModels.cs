namespace OpenSteamTool.Manager.Models;

public sealed class SteamGameInfo
{
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LibraryPath { get; set; } = string.Empty;
    public string InstallDir { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
}

public sealed class GitHubGamePackage
{
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ZipPath { get; set; } = string.Empty;
    public string ZipUrl { get; set; } = string.Empty;
    public string OriginalZipUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class GamePackageManifest
{
    public int Version { get; set; } = 1;
    public List<GitHubGamePackage> Items { get; set; } = new();
}

public sealed class GamePackageInstalledFile
{
    public string RelativePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string InstalledHash { get; set; } = string.Empty;
    public string? BackupPath { get; set; }
    public bool WasExisting { get; set; }
}

public sealed class GamePackageInstallRecord
{
    public string RecordDirectory { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string GameInstallPath { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string PackageDescription { get; set; } = string.Empty;
    public string PackageZipUrl { get; set; } = string.Empty;
    public string PackageSha256 { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string ManifestUrl { get; set; } = string.Empty;
    public DateTimeOffset InstalledAtUtc { get; set; }
    public List<GamePackageInstalledFile> Files { get; set; } = new();
}
