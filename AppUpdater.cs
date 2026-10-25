using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SonarShift;

// Portable-only updater. No elevation, shell scripts, telemetry or security-policy changes.
internal static class AppUpdater
{
    internal const string ReleasesUrl = "https://api.github.com/repos/youval1234/SignalShift/releases?per_page=100";
    internal static readonly string Cache = Path.Combine(SettingsStore.DirectoryPath, "Updates");
    internal static string StatusPath => Path.Combine(SettingsStore.DirectoryPath, "update-status.txt");
    internal static Version Current => typeof(AppUpdater).Assembly.GetName().Version!;
    internal sealed record Release(Version Version, string Url, long Size, string Hash);
    internal sealed record Plan(string Target, string OriginalHash, string Hash, int HostPid, int UiPid, bool ShowWindow);
    internal static void Status(string text) { try { Directory.CreateDirectory(SettingsStore.DirectoryPath); File.WriteAllText(StatusPath, text); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    internal static Release? Select(string json, Version current, bool? previewChannel = null)
    {
        using var document = JsonDocument.Parse(json);
        Release? best = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.GetProperty("draft").GetBoolean()) continue;
            var tag = item.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^(?:preview-|v)\d+\.\d+\.\d+$")) continue;
            if (!Version.TryParse(Regex.Replace(tag, "^(preview-|v)", "") + ".0", out var version) || version <= current || (best != null && version <= best.Version)) continue;
            // Preview installs follow previews and stable releases; stable installs follow stable only.
            var previewInstall = previewChannel ?? typeof(AppUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Contains("preview", StringComparison.OrdinalIgnoreCase) == true;
            if (item.GetProperty("prerelease").GetBoolean() && !previewInstall) continue;
            var assets = item.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == "SignalShift.exe" && a.GetProperty("state").GetString() == "uploaded").ToArray();
            if (assets.Length != 1) continue;
            var asset = assets[0];
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            var expectedUrl = "https://github.com/youval1234/SignalShift/releases/download/" + tag + "/SignalShift.exe";
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            var size = asset.GetProperty("size").GetInt64();
            if (url != expectedUrl || !Regex.IsMatch(digest, "^sha256:[0-9a-f]{64}$") || size < 1000000 || size > 300L * 1024 * 1024) continue;
            best = new(version, url, size, digest[7..]);
        }
        return best;
    }
    internal static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    internal static async Task<bool> DownloadAsync(HttpClient client, Release release, string path, CancellationToken token)
    {
        using var response = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is Uri final && (final.Scheme != "https" || final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))) throw new InvalidDataException("Unexpected update download destination.");
        if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new InvalidDataException("Update size differs from release metadata.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                total += count; if (total > release.Size) throw new InvalidDataException("Update exceeded its expected size.");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            }
            if (total != release.Size) throw new InvalidDataException("Update download is incomplete.");
        }
        if (Hash(path) != release.Hash) throw new InvalidDataException("Update checksum mismatch.");
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.ProductName != "SignalShift" || !Version.TryParse(info.FileVersion, out var version) || version != release.Version) throw new InvalidDataException("Unexpected update executable metadata.");
        File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        return true;
    }
    internal static async Task<string?> PrepareAsync(CancellationToken token)
    {
        if (Environment.GetEnvironmentVariable("SONARSHIFT_TEST_DATA") != null) return null;
#if SONARSHIFT_STORE
        return null;
#else
        using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        operationTimeout.CancelAfter(TimeSpan.FromMinutes(4)); token = operationTimeout.Token;
        var target = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(target).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return null;
        Cleanup(); Status("Checking GitHub for updates…");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(4), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SignalShift/" + Current.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        metadataTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        var json = await client.GetStringAsync(ReleasesUrl, metadataTimeout.Token).ConfigureAwait(false);
        var release = Select(json, Current);
        if (release == null) { Status("Up to date · checked " + DateTime.Now.ToString("g")); return null; }
        Status("Downloading SignalShift " + release.Version.ToString(3) + "…");
        var directory = Path.Combine(Cache, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var candidate = Path.Combine(directory, "SignalShift.exe");
            await DownloadAsync(client, release, candidate, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Check write permission before asking the running host to exit.
            var probe = Path.Combine(Path.GetDirectoryName(target)!, ".signalshift-update-" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe)) { } File.Delete(probe);
            File.Copy(target, Path.Combine(directory, "Updater.exe"));
            CopyZone(target, Path.Combine(directory, "Updater.exe"));
            return directory;
        }
        catch { try { Directory.Delete(directory, true); } catch (IOException) { } throw; }
#endif
    }
    internal static void Start(string directory, int uiPid, bool showWindow)
    {
        var target = Environment.ProcessPath!;
        var plan = new Plan(target, Hash(target), Hash(Path.Combine(directory, "SignalShift.exe")), Environment.ProcessId, uiPid, showWindow);
        var manifest = Path.Combine(directory, "plan.json"); File.WriteAllText(manifest, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(Path.Combine(directory, "Updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(manifest);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
        Status("Installing update; SignalShift will restart…");
    }
    private static void CopyZone(string source, string target)
    {
        try { File.WriteAllText(target + ":Zone.Identifier", File.ReadAllText(source + ":Zone.Identifier")); }
        catch (FileNotFoundException) { }
    }
    private static void WaitForExit(int pid, string expectedPath)
    {
        if (pid == 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update process identity changed.");
            if (!process.WaitForExit(30000)) throw new IOException("Close SignalShift windows before updating.");
        }
        catch (ArgumentException) { } // Process already exited.
    }
    internal static int Apply(string manifest)
    {
        Plan? plan = null; bool validated = false;
        try
        {
            var worker = Path.GetFullPath(Environment.ProcessPath!);
            var directory = Path.GetDirectoryName(worker)!;
            if (!directory.StartsWith(Path.GetFullPath(Cache) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(manifest) != Path.Combine(directory, "plan.json")) throw new InvalidDataException("Invalid update staging directory.");
            plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("Invalid update plan.");
            var candidate = Path.Combine(directory, "SignalShift.exe");
            if (!Path.IsPathFullyQualified(plan.Target) || !plan.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Hash(plan.Target) != plan.OriginalHash) throw new InvalidDataException("Installed executable changed after verification.");
            var prior = FileVersionInfo.GetVersionInfo(plan.Target);
            if (prior.ProductName != "SignalShift") throw new InvalidDataException("Unexpected installed executable.");
            validated = true;
            if (Hash(candidate) != plan.Hash) throw new InvalidDataException("Candidate changed after verification.");
            var info = FileVersionInfo.GetVersionInfo(candidate);
            if (prior.ProductName != "SignalShift" || info.ProductName != "SignalShift" || !Version.TryParse(info.FileVersion, out var version) || !Version.TryParse(prior.FileVersion, out var priorVersion) || version <= priorVersion) throw new InvalidDataException("Update is not a newer SignalShift executable.");
            WaitForExit(plan.HostPid, plan.Target); WaitForExit(plan.UiPid, plan.Target);
            Commit(candidate, plan.Target, plan.OriginalHash, plan.Hash, () => Restart(plan, version));
            Status("Updated to " + version.ToString(3) + " · " + DateTime.Now.ToString("g"));
            return 0;
        }
        catch (Exception ex)
        {
            Status("Update could not finish: " + ex.Message);
            if (validated && plan != null) { try { Restart(plan); } catch (Exception) { } }
            return 1;
        }
    }
    internal static void Commit(string candidate, string target, string originalHash, string newHash, Action restart)
    {
        var next = target + ".update-" + Guid.NewGuid().ToString("N");
        var backup = target + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(candidate, next); CopyZone(target, next);
            if (Hash(target) != originalHash || Hash(next) != newHash) throw new InvalidDataException("Update files changed during handoff.");
            File.Replace(next, target, backup);
            try { restart(); }
            catch { File.Replace(backup, target, null); throw; }
            try { File.Delete(backup); } catch (IOException) { }
        }
        finally { try { File.Delete(next); } catch (IOException) { } }
    }
    private static void Restart(Plan plan, Version? expected = null)
    {
        var start = new ProcessStartInfo(plan.Target) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--updated"); if (!plan.ShowWindow) start.ArgumentList.Add("--tray");
        using var process = Process.Start(start) ?? throw new IOException("Could not restart SignalShift.");
        if (expected == null) return;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (process.HasExited) throw new IOException("The updated app exited before it was ready.");
            try
            {
                using var status = JsonDocument.Parse(HostConnection.Send("status", 500));
                if (status.RootElement.GetProperty("pid").GetInt32() == process.Id && status.RootElement.GetProperty("version").GetString() == expected.ToString(3)) return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or System.Collections.Generic.KeyNotFoundException) { }
            Thread.Sleep(250);
        }
        if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
        throw new IOException("The updated app did not become ready; the previous executable was retained.");
    }
    private static void Cleanup()
    {
        if (!Directory.Exists(Cache)) return;
        foreach (var directory in Directory.EnumerateDirectories(Cache))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
