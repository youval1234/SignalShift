using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SonarShift;

internal static class UpdaterChecks
{
    private static object Item(string tag = "preview-9.0.0", string? url = null, string? hash = null, bool draft = false) => new
    {
        tag_name = tag, draft, prerelease = tag.StartsWith("preview-"),
        assets = new[] { new { name = "SignalShift.exe", state = "uploaded", size = 170000000, digest = "sha256:" + (hash ?? new string('a', 64)), browser_download_url = url ?? "https://github.com/youval1234/SignalShift/releases/download/" + tag + "/SignalShift.exe" } }
    };
    internal static void Run(Action<string, Action> check, Action<bool, string> require)
    {
        check("Updater selects newest published version and rejects drafts, downgrades and foreign download URLs", () =>
        {
            var json = JsonSerializer.Serialize(new[] { Item("preview-3.0.0"), Item("preview-9.0.0", draft: true), Item("preview-8.0.0", url: "https://example.com/SignalShift.exe"), Item("preview-1.0.0"), Item("preview-4.0.0", hash: "invalid") });
            require(AppUpdater.Select(json, new Version(2, 4, 0, 0), true)?.Version == new Version(3, 0, 0, 0), "Wrong release selected");
            require(AppUpdater.Select(json, new Version(3, 0, 0, 0), true) == null, "Updater attempted a downgrade or accepted unverified bytes");
            require(AppUpdater.Select(json, new Version(2, 4, 0, 0), false) == null, "Stable channel accepted a preview");
            require(AppUpdater.Select(JsonSerializer.Serialize(new[] { Item("v3.0.0"), Item("preview-4.0.0") }), new Version(2,4,0,0), false)?.Version == new Version(3,0,0,0), "Stable channel missed a stable update");
        });
        check("Updater rejects oversized and truncated downloads before execution", () =>
        {
            foreach (var size in new[] { 2L, 4L })
            {
                var path = Path.Combine(Path.GetTempPath(), "signalshift-update-check-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var client = new HttpClient(new Handler(() => new MemoryStream(new byte[] { 1, 2, 3 })));
                    var failed = false;
                    try { Task.Run(() => AppUpdater.DownloadAsync(client, new(new Version(3,0,0,0), "https://github.com/example", size, new string('a',64)), path, CancellationToken.None)).GetAwaiter().GetResult(); }
                    catch (InvalidDataException) { failed = true; }
                    require(failed, "Invalid download size was accepted");
                }
                finally { File.Delete(path); }
            }
        });
        check("Updater rejects altered download hashes and cancelled downloads", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "signalshift-update-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var client = new HttpClient(new Handler(() => new MemoryStream(new byte[] { 1, 2, 3 })));
                var failed = false;
                try { Task.Run(() => AppUpdater.DownloadAsync(client, new(new Version(3,0,0,0), "https://github.com/example", 3, new string('a',64)), path, CancellationToken.None)).GetAwaiter().GetResult(); }
                catch (InvalidDataException) { failed = true; }
                require(failed, "Corrupted bytes accepted"); File.Delete(path);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); failed = false;
                try { Task.Run(() => AppUpdater.DownloadAsync(client, new(new Version(3,0,0,0), "https://github.com/example", 3, new string('a',64)), path, cancelled.Token)).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { failed = true; }
                require(failed && !File.Exists(path), "Cancelled download created a candidate");
            }
            finally { File.Delete(path); }
        });
        check("Automatic-update preference migrates and survives settings export", () =>
        {
            require(JsonSerializer.Deserialize<Settings>("{}")!.AutomaticUpdates, "Old settings must enable launch checks");
            require(!SettingsTransfer.Clone(new Settings { AutomaticUpdates = false }).AutomaticUpdates, "Update opt-out was lost");
        });
        check("Updater replacement preserves download zone and rolls back failed startup", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "signalshift-swap-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try
            {
                var old = Path.Combine(directory, "installed.exe"); var candidate = Path.Combine(directory, "candidate.exe");
                File.WriteAllText(old, "original"); File.WriteAllText(candidate, "updated");
                var zone = "[ZoneTransfer]\r\nZoneId=3\r\n"; File.WriteAllText(old + ":Zone.Identifier", zone);
                var original = AppUpdater.Hash(old); var newer = AppUpdater.Hash(candidate); bool rejected = false;
                try { AppUpdater.Commit(candidate, old, original, newer, () => throw new IOException("Simulated startup failure")); }
                catch (IOException) { rejected = true; }
                require(rejected && AppUpdater.Hash(old) == original && File.ReadAllText(old + ":Zone.Identifier") == zone, "Failed startup did not restore original bytes and zone");
                rejected = false;
                try { AppUpdater.Commit(candidate, old, original, new string('0',64), () => throw new Exception("Must not run")); }
                catch (InvalidDataException) { rejected = true; }
                require(rejected && AppUpdater.Hash(old) == original, "Changed candidate overwrote installed version");
                AppUpdater.Commit(candidate, old, original, newer, () => { });
                require(AppUpdater.Hash(old) == newer && File.ReadAllText(old + ":Zone.Identifier") == zone, "Successful update lost verified bytes or security zone");
                require(Directory.GetFiles(directory).Length == 2, "Staged or backup files remained after completion");
            }
            finally { Directory.Delete(directory, true); }
        });
    }
    private sealed class Handler(Func<Stream> create) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(create()) }); }
    }
}
