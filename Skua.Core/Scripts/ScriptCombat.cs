using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Monsters;

namespace Skua.Core.Scripts;

public partial class ScriptCombat : IScriptCombat
{
    public ScriptCombat(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptOption> options,
        Lazy<IScriptWait> wait,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptMap> map,
        IScriptRunTelemetryService telemetry)
    {
        _lazyFlash = flash;
        _lazyOptions = options;
        _lazyWait = wait;
        _lazyPlayer = player;
        _lazyMap = map;
        _telemetry = telemetry;
        _messenger = StrongReferenceMessenger.Default;

        _messenger.Register<ScriptCombat, CounterAttackMessage, int>(this, (int)MessageChannels.GameEvents, CounterAttack);
        _messenger.Register<ScriptCombat, PlayerDeathMessage, int>(this, (int)MessageChannels.GameEvents, PlayerDead);
        _messenger.Register<ScriptCombat, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, ScriptStopped);
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptMap> _lazyMap;
    private readonly IMessenger _messenger;
    private readonly IScriptRunTelemetryService _telemetry;

    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptMap Map => _lazyMap.Value;

    public bool EnableCounterHandler { get; set; } = false;

    public bool StopAttacking { get; set; }

    [MethodCallBinding("world.approachTarget", GameFunction = true)]
    private void _approachTarget()
    { }

    [MethodCallBinding("untargetSelf")]
    private void _untargetSelf()
    { }

    [MethodCallBinding("world.cancelTarget", RunMethodPost = true, GameFunction = true)]
    private void _cancelTarget()
    {
        _telemetry.TrackEvent("combat.cancel_target", new { target = Player.Target?.Name, targetId = Player.Target?.MapID, map = Map.Name, cell = Player.Cell, roomId = Map.RoomID });
    }

    [MethodCallBinding("world.cancelAutoAttack", GameFunction = true)]
    private void _cancelAutoAttack()
    { }

    public void Exit()
    {
        if (Player.State == 1)
            return;
        CancelAutoAttack();
        CancelTarget();
        Map.Jump(Player.Cell, Player.Pad);
        Thread.Sleep(300);
        Map.Jump(Player.Cell, Player.Pad);
        Wait.ForCombatExit();
    }

    public bool Attack(string name)
    {
        if (StopAttacking)
        {
            CancelTarget();
            return false;
        }

        bool result = Flash.Call<bool>("attackMonsterName", name);
        _telemetry.TrackEvent("combat.attack.name", new { name, result, map = Map.Name, cell = Player.Cell, roomId = Map.RoomID });
        return result;
    }

    public bool Attack(int id)
    {
        if (StopAttacking)
        {
            CancelTarget();
            return false;
        }

        bool result = Flash.Call<bool>("attackMonsterID", id);
        _telemetry.TrackEvent("combat.attack.id", new { id, result, target = Player.Target?.Name, map = Map.Name, cell = Player.Cell, roomId = Map.RoomID });
        return result;
    }

    public bool AttackPlayer(string name)
    {
        bool result = Flash.Call<bool>("attackPlayer", name);
        _telemetry.TrackEvent("combat.attack.player", new { name, result, map = Map.Name, cell = Player.Cell, roomId = Map.RoomID });
        return result;
    }

    private void PlayerDead(ScriptCombat recipient, PlayerDeathMessage message)
    {
        recipient.StopAttacking = false;
    }

    private void ScriptStopped(ScriptCombat recipient, ScriptStoppedMessage message)
    {
        recipient.StopAttacking = false;
    }

    private Monster? _target;

    private void CounterAttack(ScriptCombat recipient, CounterAttackMessage message)
    {
        if (EnableCounterHandler)
        {
            if (message.Faded)
            {
                recipient.StopAttacking = false;
                if (recipient._target is not null)
                    recipient.Attack(recipient._target.MapID);
                recipient._target = null;
                return;
            }
            recipient.StopAttacking = true;
            recipient._target = recipient.Player.Target;
            recipient.CancelAutoAttack();
            recipient.CancelTarget();
        }
    }
}