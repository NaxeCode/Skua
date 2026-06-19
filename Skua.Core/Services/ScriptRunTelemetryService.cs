using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skua.Core.Services;

public sealed class ScriptRunTelemetryService : IScriptRunTelemetryService, IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private static readonly Regex UnsafePathChars = new("[^a-zA-Z0-9_.-]+", RegexOptions.Compiled);

    private readonly Lazy<IScriptInterface> _bot;
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _sampleTask;
    private ScriptRunState? _run;
    private TelemetrySample? _previousSample;
    private bool _wasDead;

    public ScriptRunTelemetryService(Lazy<IScriptInterface> bot)
    {
        _bot = bot;
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, PlayerDeathMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.player_death"));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, MonsterKilledMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.monster_killed", new { mapId = m.MapID }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, QuestAcceptedMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.quest_accepted", new { questId = m.QuestID }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, QuestTurninMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.quest_turnin", new { questId = m.QuestID }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.map_changed", new { map = m.Map }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, CellChangedMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.cell_changed", new { map = m.Map, cell = m.Cell, pad = m.Pad }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.item_dropped", new { itemId = m.Item.ID, name = m.Item.Name, quantity = m.Item.Quantity, addedToInventory = m.AddedToInv, quantityNow = m.QuantityNow, temp = m.Item.Temp }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, ItemBoughtMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.item_bought", new { charItemId = m.CharItemID }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, ItemSoldMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.item_sold", new { charItemId = m.CharItemID, quantitySold = m.QuantitySold, currentQuantity = m.CurrentQuantity, cost = m.Cost, isAc = m.IsAC }));
        StrongReferenceMessenger.Default.Register<ScriptRunTelemetryService, ItemAddedToBankMessage, int>(this, (int)MessageChannels.GameEvents, static (r, m) => r.TrackEvent("game.item_added_to_bank", new { itemId = m.Item.ID, name = m.Item.Name, quantityNow = m.QuantityNow }));
    }

    public string? CurrentRunDirectory
    {
        get
        {
            lock (_sync)
                return _run?.RunDirectory;
        }
    }

    public void StartRun(string scriptPath)
    {
        StopRun();

        string scriptName = Path.GetFileNameWithoutExtension(scriptPath);
        string runId = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string safeName = Sanitize(scriptName);
        string runDirectory = Path.Combine(GetLogRoot(), $"{runId}_{safeName}");

        Directory.CreateDirectory(runDirectory);

        var run = new ScriptRunState(scriptPath, scriptName, runId, runDirectory, DateTimeOffset.UtcNow);
        var cts = new CancellationTokenSource();

        lock (_sync)
        {
            _run = run;
            _previousSample = null;
            _wasDead = false;
            _cts = cts;
        }

        WriteJsonLine(run, "telemetry.jsonl", new
        {
            type = "start",
            at = run.StartedAt,
            runId = run.RunId,
            scriptName,
            scriptPath,
            runDirectory
        });
        AppendScriptLog($"[telemetry] run started: {runDirectory}");

        _sampleTask = Task.Run(() => SampleLoop(run, cts.Token));
    }

    public void StopRun(Exception? exception = null)
    {
        ScriptRunState? run;
        CancellationTokenSource? cts;
        Task? sampleTask;

        lock (_sync)
        {
            run = _run;
            cts = _cts;
            sampleTask = _sampleTask;
            _cts = null;
            _sampleTask = null;
        }

        if (run == null)
            return;

        try { cts?.Cancel(); } catch { }
        try { sampleTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { WriteSample(run, final: true); } catch { }

        DateTimeOffset stoppedAt = DateTimeOffset.UtcNow;
        double durationSeconds = Math.Max(0.001, (stoppedAt - run.StartedAt).TotalSeconds);
        double hours = durationSeconds / 3600.0;

        var summary = new
        {
            type = "stop",
            at = stoppedAt,
            runId = run.RunId,
            scriptName = run.ScriptName,
            scriptPath = run.ScriptPath,
            runDirectory = run.RunDirectory,
            durationSeconds,
            xpGained = run.XpGained,
            xpPerHour = run.XpGained / Math.Max(0.001, hours),
            goldGained = run.GoldGained,
            goldPerHour = run.GoldGained / Math.Max(0.001, hours),
            deaths = run.Deaths,
            mapChanges = run.MapChanges,
            exception = exception?.GetType().FullName,
            exceptionMessage = exception?.Message
        };

        WriteJsonLine(run, "telemetry.jsonl", summary);
        string summaryJson = JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine;
        File.WriteAllText(Path.Combine(run.RunDirectory, "summary.json"), summaryJson);
        TryWriteLatestRunPointers(run, summaryJson);
        AppendScriptLog($"[telemetry] run stopped: xpGained={run.XpGained:N0}, xpPerHour={(run.XpGained / Math.Max(0.001, hours)):N0}, deaths={run.Deaths}, log={run.RunDirectory}");

        lock (_sync)
        {
            if (ReferenceEquals(_run, run))
                _run = null;
        }

        cts?.Dispose();
    }

    public void AppendScriptLog(string message)
    {
        if (message is null)
            return;

        ScriptRunState? run;
        lock (_sync)
            run = _run;

        if (run == null)
            return;

        try
        {
            string line = DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine;
            lock (run.FileLock)
                File.AppendAllText(Path.Combine(run.RunDirectory, "script.log"), line);
        }
        catch { }
    }

    public void TrackEvent(string type, object? data = null)
    {
        if (string.IsNullOrWhiteSpace(type))
            return;

        ScriptRunState? run;
        lock (_sync)
            run = _run;

        if (run == null)
            return;

        try
        {
            WriteJsonLine(run, "telemetry.jsonl", new
            {
                type,
                at = DateTimeOffset.UtcNow,
                runId = run.RunId,
                scriptName = run.ScriptName,
                data
            });
        }
        catch { }
    }

    private async Task SampleLoop(ScriptRunState run, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { WriteSample(run, final: false); }
            catch { }

            try { await Task.Delay(SampleInterval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void WriteSample(ScriptRunState run, bool final)
    {
        TelemetrySample sample = CaptureSample(final);
        TelemetrySample? previous;
        lock (_sync)
        {
            previous = _previousSample;
            _previousSample = sample;
        }

        int xpDelta = 0;
        int goldDelta = 0;

        if (previous != null)
        {
            xpDelta = ComputeXpDelta(previous, sample);
            goldDelta = sample.Gold - previous.Gold;
            if (xpDelta > 0)
                run.XpGained += xpDelta;
            if (goldDelta != 0)
                run.GoldGained += goldDelta;
            if (!string.Equals(previous.Map, sample.Map, StringComparison.OrdinalIgnoreCase))
                run.MapChanges++;
        }

        if (!sample.Alive && !_wasDead)
            run.Deaths++;
        _wasDead = !sample.Alive;

        double durationSeconds = Math.Max(0.001, (sample.At - run.StartedAt).TotalSeconds);
        double hours = durationSeconds / 3600.0;

        WriteJsonLine(run, "telemetry.jsonl", new
        {
            type = final ? "final-sample" : "sample",
            at = sample.At,
            runId = run.RunId,
            scriptName = run.ScriptName,
            sample.Level,
            sample.Xp,
            sample.RequiredXp,
            xpDelta,
            xpGained = run.XpGained,
            xpPerHour = run.XpGained / Math.Max(0.001, hours),
            sample.Gold,
            goldDelta,
            goldGained = run.GoldGained,
            goldPerHour = run.GoldGained / Math.Max(0.001, hours),
            sample.ClassName,
            sample.ClassRank,
            sample.Health,
            sample.MaxHealth,
            sample.Mana,
            sample.MaxMana,
            sample.Alive,
            sample.InCombat,
            sample.Map,
            sample.Cell,
            sample.Pad,
            sample.RoomId,
            sample.Target,
            sample.Enemies,
            sample.XpBoost,
            sample.RepBoost,
            sample.ClassBoost,
            sample.GoldBoost,
            deaths = run.Deaths,
            mapChanges = run.MapChanges
        });
    }

    private TelemetrySample CaptureSample(bool final)
    {
        IScriptInterface bot = _bot.Value;

        string enemies = string.Empty;
        try
        {
            enemies = string.Join(" | ", bot.Monsters.CurrentAvailableMonsters
                .Where(m => m.Alive)
                .Take(12)
                .Select(m => $"{m.Name}#{m.MapID}@{m.Cell} {m.HP}/{m.MaxHP}"));
        }
        catch { }

        string target = "none";
        try
        {
            if (bot.Player.Target != null)
                target = $"{bot.Player.Target.Name}#{bot.Player.Target.MapID} {bot.Player.Target.HP}/{bot.Player.Target.MaxHP}";
        }
        catch { }

        return new TelemetrySample(
            DateTimeOffset.UtcNow,
            bot.Player.Level,
            bot.Player.XP,
            bot.Player.RequiredXP,
            bot.Player.Gold,
            bot.Player.CurrentClass?.Name ?? string.Empty,
            bot.Player.CurrentClassRank,
            bot.Player.Health,
            bot.Player.MaxHealth,
            bot.Player.Mana,
            bot.Player.MaxMana,
            bot.Player.Alive,
            bot.Player.InCombat,
            Safe(() => bot.Map.Name, string.Empty),
            Safe(() => bot.Player.Cell, string.Empty),
            Safe(() => bot.Player.Pad, string.Empty),
            Safe(() => bot.Map.RoomID, 0),
            target,
            enemies,
            Safe(() => bot.Boosts.IsBoostActive(BoostType.Experience), false),
            Safe(() => bot.Boosts.IsBoostActive(BoostType.Reputation), false),
            Safe(() => bot.Boosts.IsBoostActive(BoostType.Class), false),
            Safe(() => bot.Boosts.IsBoostActive(BoostType.Gold), false));
    }

    private static int ComputeXpDelta(TelemetrySample previous, TelemetrySample current)
    {
        if (current.Level == previous.Level)
            return Math.Max(0, current.Xp - previous.Xp);

        if (current.Level > previous.Level)
        {
            int delta = Math.Max(0, previous.RequiredXp - previous.Xp) + Math.Max(0, current.Xp);
            return delta;
        }

        return 0;
    }

    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }

    private static void WriteJsonLine(ScriptRunState run, string fileName, object value)
    {
        try
        {
            string path = Path.Combine(run.RunDirectory, fileName);
            string line = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
            lock (run.FileLock)
                File.AppendAllText(path, line);
        }
        catch { }
    }

    private static void TryWriteLatestRunPointers(ScriptRunState run, string summaryJson)
    {
        try
        {
            string root = GetLogRoot();
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "latest-run.txt"), run.RunDirectory + Environment.NewLine);
            File.WriteAllText(Path.Combine(root, "latest-summary.json"), summaryJson);
        }
        catch { }
    }

    private static string GetLogRoot()
    {
        string stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(stateHome, "skua", "logs", "script-runs");
    }

    private static string Sanitize(string value)
    {
        string cleaned = UnsafePathChars.Replace(value, "_").Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "script" : cleaned;
    }

    public void Dispose()
    {
        StrongReferenceMessenger.Default.UnregisterAll(this);
        StopRun();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private sealed class ScriptRunState
    {
        public ScriptRunState(string scriptPath, string scriptName, string runId, string runDirectory, DateTimeOffset startedAt)
        {
            ScriptPath = scriptPath;
            ScriptName = scriptName;
            RunId = runId;
            RunDirectory = runDirectory;
            StartedAt = startedAt;
        }

        public string ScriptPath { get; }
        public string ScriptName { get; }
        public string RunId { get; }
        public string RunDirectory { get; }
        public DateTimeOffset StartedAt { get; }
        public object FileLock { get; } = new();
        public int XpGained { get; set; }
        public int GoldGained { get; set; }
        public int Deaths { get; set; }
        public int MapChanges { get; set; }
    }

    private sealed record TelemetrySample(
        DateTimeOffset At,
        int Level,
        int Xp,
        int RequiredXp,
        int Gold,
        string ClassName,
        int ClassRank,
        int Health,
        int MaxHealth,
        int Mana,
        int MaxMana,
        bool Alive,
        bool InCombat,
        string Map,
        string Cell,
        string Pad,
        int RoomId,
        string Target,
        string Enemies,
        bool XpBoost,
        bool RepBoost,
        bool ClassBoost,
        bool GoldBoost);
}
