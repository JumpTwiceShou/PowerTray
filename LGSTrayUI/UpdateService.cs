using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayUI;

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/JumpTwiceShou/PowerTray/releases/latest";
    private const string InstallerEditionMarkerFileName = "installer-edition.txt";
    private const string LightInstallerAssetName = "PowerTraySetup.exe";
    private const string FullInstallerAssetName = "PowerTraySetup-full.exe";
    private const string ChecksumAssetSuffix = ".sha256";
    private readonly LocalizationService _loc;
    private readonly HttpClient _httpClient = new();

    public UpdateService(LocalizationService loc)
    {
        _loc = loc;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerTray.NativeBattery");
    }

    public Task CheckForUpdatesAsync()
    {
        return CheckForUpdatesAsync(showAlreadyLatest: true, showFailures: true);
    }

    public async Task CheckForUpdatesAsync(bool showAlreadyLatest, bool showFailures)
    {
        try
        {
            GitHubRelease release = await GetLatestReleaseAsync();
            Version current = GetCurrentVersion();
            Version latest = ParseVersion(release.TagName);

            if (latest <= current)
            {
                if (showAlreadyLatest)
                {
                    ShowMessage(_loc["AlreadyLatest"], _loc["MenuCheckUpdates"]);
                }

                return;
            }

            InstallerEdition installerEdition = GetInstalledInstallerEdition();
            GitHubAsset? installer = SelectInstallerAsset(release.Assets, installerEdition);
            GitHubAsset? checksum = installer == null ? null : SelectChecksumAsset(release.Assets, installer.Name);

            if (installer == null || checksum == null ||
                string.IsNullOrWhiteSpace(installer.BrowserDownloadUrl) ||
                string.IsNullOrWhiteSpace(checksum.BrowserDownloadUrl))
            {
                if (showFailures)
                {
                    ShowMessage(_loc["NoInstallerAsset"], _loc["UpdateCheckFailed"]);
                }

                return;
            }

            string downloadChoice = ShowOptions(
                string.Format(_loc["UpdateAvailableBody"], latest),
                _loc["UpdateAvailableTitle"],
                [
                    new(_loc["DownloadUpdate"], "download", IsDefault: true),
                    new(_loc["Cancel"], "cancel", IsCancel: true),
                ],
                _loc["UpdateAvailableDetail"]);

            if (downloadChoice != "download")
            {
                return;
            }

            string installerPath = await DownloadInstallerAsync(installer, checksum, latest, installerEdition);
            string result = ShowOptions(
                string.Format(_loc["UpdateDownloadedBody"], latest),
                _loc["UpdateDownloadedTitle"],
                [
                    new(_loc["RunInstaller"], "run", IsDefault: true),
                    new(_loc["OpenFolder"], "folder"),
                    new(_loc["Cancel"], "cancel", IsCancel: true),
                ],
                string.Format(_loc["UpdateDownloadedDetail"], installerPath));

            if (result == "run")
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            }
            else if (result == "folder")
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{installerPath}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            if (showFailures)
            {
                ShowMessage(_loc["UpdateCheckFailed"], _loc["MenuCheckUpdates"], ex.Message);
            }
        }
    }

    private static void ShowMessage(string message, string title, string? detail = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
            ThemedMessageBox.ShowOptions(message, title, [new(ThemedButtonText("OK", "OK"), "ok", IsDefault: true)], detail));
    }

    private static string ShowOptions(string message, string title, IReadOnlyList<ThemedDialogOption> options, string? detail = null)
    {
        return Application.Current.Dispatcher.Invoke(() =>
            ThemedMessageBox.ShowOptions(message, title, options, detail));
    }

    private static string ThemedButtonText(string key, string fallback)
    {
        try
        {
            string? translated = ThemedMessageBox.Translate?.Invoke(key);
            return string.IsNullOrWhiteSpace(translated) ? fallback : translated;
        }
        catch
        {
            return fallback;
        }
    }

    public static InstallerEdition GetInstalledInstallerEdition()
    {
        string markerPath = Path.Combine(AppContext.BaseDirectory, InstallerEditionMarkerFileName);
        try
        {
            if (File.Exists(markerPath))
            {
                string marker = File.ReadAllText(markerPath).Trim();
                if (IsFullEditionMarker(marker))
                {
                    return InstallerEdition.Full;
                }

                if (IsLightEditionMarker(marker))
                {
                    return InstallerEdition.Light;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to directory inspection if the marker cannot be read.
        }

        return LooksSelfContained(AppContext.BaseDirectory) ? InstallerEdition.Full : InstallerEdition.Light;
    }

    internal static string? SelectInstallerAssetName(IEnumerable<string> assetNames, InstallerEdition edition)
    {
        return SelectInstallerAsset(assetNames.Select(name => new GitHubAsset { Name = name }), edition)?.Name;
    }

    private static GitHubAsset? SelectInstallerAsset(IEnumerable<GitHubAsset> assets, InstallerEdition edition)
    {
        GitHubAsset[] installers = assets
            .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(x => IsPowerTrayInstallerAsset(x.Name))
            .ToArray();

        GitHubAsset? preferred = edition == InstallerEdition.Full
            ? installers.FirstOrDefault(x => IsFullInstallerAsset(x.Name))
            : installers.FirstOrDefault(x => IsLightInstallerAsset(x.Name));

        if (preferred != null)
        {
            return preferred;
        }

        return null;
    }

    private static GitHubAsset? SelectChecksumAsset(IEnumerable<GitHubAsset> assets, string installerAssetName)
    {
        string checksumName = installerAssetName + ChecksumAssetSuffix;
        return assets.FirstOrDefault(x => Path.GetFileName(x.Name).Equals(checksumName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<GitHubRelease> GetLatestReleaseAsync()
    {
        using Stream stream = await _httpClient.GetStreamAsync(LatestReleaseUrl);
        GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream);
        if (release == null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new InvalidDataException(_loc["UpdateCheckFailed"]);
        }

        return release;
    }

    private async Task<string> DownloadInstallerAsync(GitHubAsset installer, GitHubAsset checksum, Version latest, InstallerEdition edition)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        string editionSuffix = edition == InstallerEdition.Full ? "-full" : string.Empty;
        string fileName = $"PowerTraySetup-{latest}{editionSuffix}.exe";
        string targetPath = Path.Combine(downloads, fileName);
        string tempPath = targetPath + ".download";

        try
        {
            string checksumText = await _httpClient.GetStringAsync(checksum.BrowserDownloadUrl);
            if (!TryParseSha256Checksum(checksumText, installer.Name, out string expectedSha256))
            {
                throw new InvalidDataException(_loc["UpdateChecksumInvalid"]);
            }

            await using Stream remote = await _httpClient.GetStreamAsync(installer.BrowserDownloadUrl);
            await using FileStream local = File.Create(tempPath);
            await remote.CopyToAsync(local);

            string actualSha256 = await ComputeFileSha256Async(tempPath);
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(_loc["UpdateChecksumMismatch"]);
            }

            File.Move(tempPath, targetPath, true);
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            throw new IOException($"{_loc["UpdateDownloadFailed"]}: {ex.Message}", ex);
        }

        return targetPath;
    }

    private static bool IsPowerTrayInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return IsLightInstallerAsset(fileName) || IsFullInstallerAsset(fileName);
    }

    private static bool IsFullInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return fileName.Equals(FullInstallerAssetName, StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(fileName, @"^PowerTraySetup-(?:full-v?\d+(?:\.\d+){1,3}|v?\d+(?:\.\d+){1,3}-full)\.exe$", RegexOptions.IgnoreCase);
    }

    private static bool IsLightInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return fileName.Equals(LightInstallerAssetName, StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(fileName, @"^PowerTraySetup-v?\d+(?:\.\d+){1,3}(?:-light)?\.exe$", RegexOptions.IgnoreCase);
    }

    internal static bool TryParseSha256Checksum(string checksumText, string expectedFileName, out string expectedSha256)
    {
        expectedSha256 = string.Empty;
        string normalizedExpectedFileName = Path.GetFileName(expectedFileName);
        foreach (string rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string hash = parts[0];
            string fileName = Path.GetFileName(parts[^1].TrimStart('*'));
            if (IsSha256Hex(hash) && fileName.Equals(normalizedExpectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                expectedSha256 = hash.ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(Uri.IsHexDigit);
    }

    private static async Task<string> ComputeFileSha256Async(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup for failed downloads.
        }
    }

    private static bool IsFullEditionMarker(string marker)
    {
        return marker.Equals("full", StringComparison.OrdinalIgnoreCase)
               || marker.Equals("self-contained", StringComparison.OrdinalIgnoreCase)
               || marker.Equals("self-contained-full", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLightEditionMarker(string marker)
    {
        return marker.Equals("light", StringComparison.OrdinalIgnoreCase)
               || marker.Equals("framework-dependent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksSelfContained(string baseDirectory)
    {
        string[] runtimeFiles =
        [
            "hostfxr.dll",
            "hostpolicy.dll",
            "coreclr.dll",
            "clrjit.dll",
            "System.Private.CoreLib.dll",
        ];

        return runtimeFiles.Any(fileName => File.Exists(Path.Combine(baseDirectory, fileName)));
    }

    private static Version GetCurrentVersion()
    {
#if DEBUG
        string? overrideVersion = Environment.GetEnvironmentVariable("POWERTRAY_TEST_CURRENT_VERSION");
        if (!string.IsNullOrWhiteSpace(overrideVersion))
        {
            return ParseVersion(overrideVersion);
        }
#endif
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        string rawVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString()
                            ?? "0.0.0";
        return ParseVersion(rawVersion);
    }

    private static Version ParseVersion(string? rawVersion)
    {
        string normalized = (rawVersion ?? "0.0.0").Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        normalized = normalized.Split('+')[0].Split('-')[0];
        return Version.TryParse(normalized, out Version? version) ? version : new Version(0, 0, 0);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

public enum InstallerEdition
{
    Light,
    Full,
}
