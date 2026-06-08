using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayUI;

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/JumpTwiceShou/PowerTray/releases/latest";
    private const string InstallerEditionMarkerFileName = "installer-edition.txt";
    private const string LightInstallerAssetName = "PowerTraySetup.exe";
    private const string FullInstallerAssetName = "PowerTraySetup-full.exe";
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

            if (installer == null || string.IsNullOrWhiteSpace(installer.BrowserDownloadUrl))
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
                ]);

            if (downloadChoice != "download")
            {
                return;
            }

            string installerPath = await DownloadInstallerAsync(installer, latest, installerEdition);
            string result = ShowOptions(
                string.Format(_loc["UpdateDownloadedBody"], latest),
                _loc["UpdateDownloadedTitle"],
                [
                    new(_loc["RunInstaller"], "run", IsDefault: true),
                    new(_loc["OpenFolder"], "folder"),
                    new(_loc["Cancel"], "cancel", IsCancel: true),
                ]);

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
                ShowMessage($"{_loc["UpdateCheckFailed"]}\n\n{ex.Message}", _loc["MenuCheckUpdates"]);
            }
        }
    }

    private static void ShowMessage(string message, string title)
    {
        Application.Current.Dispatcher.Invoke(() =>
            ThemedMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxResult.OK));
    }

    private static string ShowOptions(string message, string title, IReadOnlyList<ThemedDialogOption> options)
    {
        return Application.Current.Dispatcher.Invoke(() =>
            ThemedMessageBox.ShowOptions(message, title, options));
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

        return installers.FirstOrDefault()
               ?? assets.FirstOrDefault(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
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

    private async Task<string> DownloadInstallerAsync(GitHubAsset installer, Version latest, InstallerEdition edition)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        string editionSuffix = edition == InstallerEdition.Full ? "-full" : string.Empty;
        string fileName = $"PowerTraySetup-{latest}{editionSuffix}.exe";
        string targetPath = Path.Combine(downloads, fileName);
        string tempPath = targetPath + ".download";

        try
        {
            await using Stream remote = await _httpClient.GetStreamAsync(installer.BrowserDownloadUrl);
            await using FileStream local = File.Create(tempPath);
            await remote.CopyToAsync(local);
        }
        catch (Exception ex)
        {
            throw new IOException($"{_loc["UpdateDownloadFailed"]}: {ex.Message}", ex);
        }

        File.Move(tempPath, targetPath, true);
        return targetPath;
    }

    private static bool IsPowerTrayInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return fileName.StartsWith("PowerTraySetup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFullInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return fileName.Equals(FullInstallerAssetName, StringComparison.OrdinalIgnoreCase)
               || (stem.StartsWith("PowerTraySetup", StringComparison.OrdinalIgnoreCase)
                   && stem.Contains("full", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLightInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        return fileName.Equals(LightInstallerAssetName, StringComparison.OrdinalIgnoreCase)
               || (stem.StartsWith("PowerTraySetup", StringComparison.OrdinalIgnoreCase)
                   && !stem.Contains("full", StringComparison.OrdinalIgnoreCase));
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
