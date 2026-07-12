using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayUI;

public sealed class UpdateService : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/JumpTwiceShou/PowerTray/releases/latest";
    private const string InstallerEditionMarkerFileName = "installer-edition.txt";
    private const string LightInstallerAssetName = "PowerTraySetup.exe";
    private const string FullInstallerAssetName = "PowerTraySetup-full.exe";
    private const string ChecksumAssetSuffix = ".sha256";
    private const string SignatureAssetSuffix = ".sig";
    private const string UpdateSigningPublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0G2H3dyoVsbph9xMRywEJb5BDhdQGQOrJcNwdwy6SDCautgU+Km+PIFk/sYDP3cA5IeJlcmcJoSOkb08Ja/xDw==";

    private static readonly HashSet<string> TrustedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    private readonly LocalizationService _loc;
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _updateLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await CheckForUpdatesCoreAsync(showAlreadyLatest, showFailures);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        _updateLock.Dispose();
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
        }

        return LooksSelfContained(AppContext.BaseDirectory) ? InstallerEdition.Full : InstallerEdition.Light;
    }

    internal static string? SelectInstallerAssetName(IEnumerable<string> assetNames, InstallerEdition edition)
    {
        return SelectInstallerAsset(assetNames.Select(name => new GitHubAsset { Name = name }), edition)?.Name;
    }

    internal static bool IsTrustedDownloadUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               TrustedDownloadHosts.Contains(uri.IdnHost) &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               uri.Port == 443;
    }

    internal static bool TryParseSha256Checksum(string checksumText, string expectedFileName, out string expectedSha256)
    {
        expectedSha256 = string.Empty;
        string normalizedExpectedFileName = Path.GetFileName(expectedFileName);
        foreach (string rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = rawLine.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            string hash = parts[0];
            string fileName = Path.GetFileName(parts[^1].TrimStart('*'));
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit) &&
                fileName.Equals(normalizedExpectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                expectedSha256 = hash.ToLowerInvariant();
                return true;
            }
        }

        return false;
    }

    internal static async Task<bool> VerifyFileHashAsync(string path, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
        {
            return false;
        }

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        return actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CheckForUpdatesCoreAsync(bool showAlreadyLatest, bool showFailures)
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

            InstallerEdition edition = GetInstalledInstallerEdition();
            GitHubAsset? installer = SelectInstallerAsset(release.Assets, edition);
            GitHubAsset? checksum = installer == null ? null : SelectChecksumAsset(release.Assets, installer.Name);
            GitHubAsset? signature = checksum == null ? null : SelectSignatureAsset(release.Assets, checksum.Name);
            if (installer == null || checksum == null || signature == null ||
                !IsTrustedDownloadUri(installer.BrowserDownloadUrl) ||
                !IsTrustedDownloadUri(checksum.BrowserDownloadUrl) ||
                !IsTrustedDownloadUri(signature.BrowserDownloadUrl))
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
                _loc["UpdateAvailableDetail"]
            );
            if (downloadChoice != "download")
            {
                return;
            }

            DownloadedInstaller downloaded = await DownloadInstallerAsync(installer, checksum, signature, latest, edition);
            string result = ShowOptions(
                string.Format(_loc["UpdateDownloadedBody"], latest),
                _loc["UpdateDownloadedTitle"],
                [
                    new(_loc["RunInstaller"], "run", IsDefault: true),
                    new(_loc["OpenFolder"], "folder"),
                    new(_loc["Cancel"], "cancel", IsCancel: true),
                ],
                string.Format(_loc["UpdateDownloadedDetail"], downloaded.Path)
            );

            if (result == "run")
            {
                await RunVerifiedInstallerAsync(downloaded);
            }
            else if (result == "folder")
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{downloaded.Path}\"")
                {
                    UseShellExecute = true,
                });
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

    private async Task<GitHubRelease> GetLatestReleaseAsync()
    {
        if (!IsTrustedDownloadUri(LatestReleaseUrl))
        {
            throw new InvalidOperationException("The configured release API URL is not trusted.");
        }

        using HttpResponseMessage response = await GetTrustedResponseAsync(
            new Uri(LatestReleaseUrl),
            HttpCompletionOption.ResponseHeadersRead
        );
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        GitHubRelease? release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream);
        if (release == null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new InvalidDataException(_loc["UpdateCheckFailed"]);
        }
        return release;
    }

    private async Task<DownloadedInstaller> DownloadInstallerAsync(
        GitHubAsset installer,
        GitHubAsset checksum,
        GitHubAsset signature,
        Version latest,
        InstallerEdition edition
    )
    {
        Uri installerUri = RequireTrustedUri(installer.BrowserDownloadUrl);
        Uri checksumUri = RequireTrustedUri(checksum.BrowserDownloadUrl);
        Uri signatureUri = RequireTrustedUri(signature.BrowserDownloadUrl);
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerTray",
            "Updates"
        );
        Directory.CreateDirectory(updateDirectory);
        string temporaryPath = Path.Combine(updateDirectory, $"download-{Guid.NewGuid():N}.tmp");

        try
        {
            using HttpResponseMessage checksumResponse = await GetTrustedResponseAsync(
                checksumUri,
                HttpCompletionOption.ResponseContentRead
            );
            byte[] checksumBytes = await checksumResponse.Content.ReadAsByteArrayAsync();
            using HttpResponseMessage signatureResponse = await GetTrustedResponseAsync(
                signatureUri,
                HttpCompletionOption.ResponseContentRead
            );
            byte[] signatureBytes = await signatureResponse.Content.ReadAsByteArrayAsync();
            if (!await VerifyChecksumSignatureAsync(checksumBytes, signatureBytes))
            {
                throw new InvalidDataException(_loc["UpdateChecksumInvalid"]);
            }

            string checksumText = Encoding.UTF8.GetString(checksumBytes).TrimStart('\uFEFF');
            if (!TryParseSha256Checksum(checksumText, installer.Name, out string expectedSha256))
            {
                throw new InvalidDataException(_loc["UpdateChecksumInvalid"]);
            }

            using HttpResponseMessage response = await GetTrustedResponseAsync(
                installerUri,
                HttpCompletionOption.ResponseHeadersRead
            );
            await using (Stream remote = await response.Content.ReadAsStreamAsync())
            await using (FileStream local = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough
            ))
            {
                await remote.CopyToAsync(local);
                await local.FlushAsync();
            }

            if (!await VerifyFileHashAsync(temporaryPath, expectedSha256))
            {
                throw new InvalidDataException(_loc["UpdateChecksumMismatch"]);
            }

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            string editionSuffix = edition == InstallerEdition.Full ? "-full" : string.Empty;
            string targetPath = Path.Combine(downloads, $"PowerTraySetup-{latest}{editionSuffix}.exe");
            File.Move(temporaryPath, targetPath, overwrite: true);
            return new DownloadedInstaller(targetPath, expectedSha256);
        }
        catch (Exception ex)
        {
            TryDeleteFile(temporaryPath);
            throw new IOException($"{_loc["UpdateDownloadFailed"]}: {ex.Message}", ex);
        }
    }

    internal static Task<bool> VerifyChecksumSignatureAsync(byte[] checksumBytes, byte[] signatureBytes)
    {
        return VerifyChecksumSignatureAsync(checksumBytes, signatureBytes, UpdateSigningPublicKeyBase64);
    }

    internal static Task<bool> VerifyChecksumSignatureAsync(
        byte[] checksumBytes,
        byte[] signatureBytes,
        string publicKeyBase64
    )
    {
        if (checksumBytes.Length == 0 || signatureBytes.Length != 64 || string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            return Task.FromResult(false);
        }

        try
        {
            byte[] publicKey = Convert.FromBase64String(publicKeyBase64);
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
            bool valid = bytesRead == publicKey.Length && verifier.VerifyData(
                checksumBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            );
            return Task.FromResult(valid);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return Task.FromResult(false);
        }
    }

    private async Task RunVerifiedInstallerAsync(DownloadedInstaller downloaded)
    {
        await using FileStream lockedFile = new(
            downloaded.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        string actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(lockedFile)).ToLowerInvariant();
        if (!actualSha256.Equals(downloaded.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(_loc["UpdateChecksumMismatch"]);
        }

        lockedFile.Position = 0;
        Process? process = Process.Start(new ProcessStartInfo(downloaded.Path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(downloaded.Path),
        });
        if (process == null)
        {
            throw new InvalidOperationException("The installer process did not start.");
        }
    }

    private async Task<HttpResponseMessage> GetTrustedResponseAsync(
        Uri requestUri,
        HttpCompletionOption completionOption
    )
    {
        if (!IsTrustedDownloadUri(requestUri.AbsoluteUri))
        {
            throw new InvalidDataException("The update request URL is not trusted.");
        }

        HttpResponseMessage response = await _httpClient.GetAsync(requestUri, completionOption);
        try
        {
            Uri? finalUri = response.RequestMessage?.RequestUri;
            if (finalUri == null || !IsTrustedDownloadUri(finalUri.AbsoluteUri))
            {
                throw new InvalidDataException("The update request redirected to an untrusted host.");
            }

            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static GitHubAsset? SelectInstallerAsset(IEnumerable<GitHubAsset> assets, InstallerEdition edition)
    {
        GitHubAsset[] installers = assets
            .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(asset => IsPowerTrayInstallerAsset(asset.Name))
            .ToArray();
        return edition == InstallerEdition.Full
            ? installers.FirstOrDefault(asset => IsFullInstallerAsset(asset.Name))
            : installers.FirstOrDefault(asset => IsLightInstallerAsset(asset.Name));
    }

    private static GitHubAsset? SelectChecksumAsset(IEnumerable<GitHubAsset> assets, string installerAssetName)
    {
        string checksumName = installerAssetName + ChecksumAssetSuffix;
        return assets.FirstOrDefault(asset =>
            Path.GetFileName(asset.Name).Equals(checksumName, StringComparison.OrdinalIgnoreCase));
    }

    private static GitHubAsset? SelectSignatureAsset(IEnumerable<GitHubAsset> assets, string checksumAssetName)
    {
        string signatureName = checksumAssetName + SignatureAssetSuffix;
        return assets.FirstOrDefault(asset =>
            Path.GetFileName(asset.Name).Equals(signatureName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPowerTrayInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return IsLightInstallerAsset(fileName) || IsFullInstallerAsset(fileName);
    }

    private static bool IsFullInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return fileName.Equals(FullInstallerAssetName, StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(
                   fileName,
                   @"^PowerTraySetup-(?:full-v?\d+(?:\.\d+){1,3}|v?\d+(?:\.\d+){1,3}-full)\.exe$",
                   RegexOptions.IgnoreCase
               );
    }

    private static bool IsLightInstallerAsset(string assetName)
    {
        string fileName = Path.GetFileName(assetName);
        return fileName.Equals(LightInstallerAssetName, StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(
                   fileName,
                   @"^PowerTraySetup-v?\d+(?:\.\d+){1,3}(?:-light)?\.exe$",
                   RegexOptions.IgnoreCase
               );
    }

    private static Uri RequireTrustedUri(string value)
    {
        if (!IsTrustedDownloadUri(value))
        {
            throw new InvalidDataException("The update asset URL is not a trusted GitHub HTTPS URL.");
        }
        return new Uri(value, UriKind.Absolute);
    }

    private static void ShowMessage(string message, string title, string? detail = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
            ThemedMessageBox.ShowOptions(
                message,
                title,
                [new(ThemedButtonText("OK", "OK"), "ok", IsDefault: true)],
                detail
            ));
    }

    private static string ShowOptions(
        string message,
        string title,
        IReadOnlyList<ThemedDialogOption> options,
        string? detail = null
    )
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
        }
    }

    private static bool IsFullEditionMarker(string marker) =>
        marker.Equals("full", StringComparison.OrdinalIgnoreCase) ||
        marker.Equals("self-contained", StringComparison.OrdinalIgnoreCase) ||
        marker.Equals("self-contained-full", StringComparison.OrdinalIgnoreCase);

    private static bool IsLightEditionMarker(string marker) =>
        marker.Equals("light", StringComparison.OrdinalIgnoreCase) ||
        marker.Equals("framework-dependent", StringComparison.OrdinalIgnoreCase);

    private static bool LooksSelfContained(string baseDirectory)
    {
        string[] runtimeFiles = ["hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "System.Private.CoreLib.dll"];
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

    private sealed record DownloadedInstaller(string Path, string ExpectedSha256);

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
