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
    private readonly LocalizationService _loc;
    private readonly HttpClient _httpClient = new();

    public UpdateService(LocalizationService loc)
    {
        _loc = loc;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerTray.NativeBattery");
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            GitHubRelease release = await GetLatestReleaseAsync();
            Version current = GetCurrentVersion();
            Version latest = ParseVersion(release.TagName);

            if (latest <= current)
            {
                ShowMessage(_loc["AlreadyLatest"], _loc["MenuCheckUpdates"]);
                return;
            }

            GitHubAsset? installer = release.Assets
                .Where(x => x.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Name.Contains("PowerTraySetup", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (installer == null || string.IsNullOrWhiteSpace(installer.BrowserDownloadUrl))
            {
                ShowMessage(_loc["NoInstallerAsset"], _loc["UpdateCheckFailed"]);
                return;
            }

            string installerPath = await DownloadInstallerAsync(installer, latest);
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
            ShowMessage($"{_loc["UpdateCheckFailed"]}\n\n{ex.Message}", _loc["MenuCheckUpdates"]);
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

    private async Task<string> DownloadInstallerAsync(GitHubAsset installer, Version latest)
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloads);
        string fileName = $"PowerTraySetup-{latest}.exe";
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
