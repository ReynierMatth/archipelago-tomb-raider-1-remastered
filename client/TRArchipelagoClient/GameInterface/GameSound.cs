using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Plays sound effects inside the attached game process.
///
/// Reaches the engine's own <c>Sound_Effect(int32 sfx, const XYZ_32 *pos,
/// uint32 flags)</c> (tomb1.dll+0x88480) through <see cref="ProcessMemory.CallRemote3"/>,
/// because the function needs three register arguments — RCX = sound id,
/// RDX = position pointer (NULL for a non-positional sound), R8 = flags —
/// while a plain remote call only supplies RCX. All other threads of the game
/// are suspended for the duration of the call, so the play cannot race the
/// mixer, and a prologue signature check refuses the call outright when the
/// build does not match (same guard policy as Inv_AddItem).
///
/// Sound ids are the engine's own SFX_* catalog ids; <see cref="TR1RMemoryMap.SfxSecret"/>
/// (70) is the secret chime — the same sting the game plays when Lara finds a
/// vanilla secret. Success is judged by the call completing (the return value
/// is the sample slot, -1 when the id is silent in this level), not by an exit
/// code, so a silenced id still reports as played.
/// </summary>
public class GameSound
{
    /// <summary>Minimum spacing between plays, so a burst of checks cannot machine-gun the chime.</summary>
    private const int ThrottleMs = 300;

    private readonly ProcessMemory _memory;
    private long _lastPlayTicks;
    private bool _signatureRefused;

    public GameSound(ProcessMemory memory)
    {
        _memory = memory;
    }

    /// <summary>
    /// Plays a sound effect in-game. Returns false when the call was refused
    /// (not attached / signature mismatch) or failed; a throttled play also
    /// returns false but is not an error worth warning about.
    /// </summary>
    public bool Play(int sfxId, string what)
    {
        if (_signatureRefused)
            return false;

        IntPtr t1 = _memory.Tomb1Base;
        if (t1 == IntPtr.Zero || !_memory.IsAttached)
            return false;

        if (!EnsureSignature())
        {
            _signatureRefused = true;
            return false;
        }

        long now = Environment.TickCount64;
        if (now - _lastPlayTicks < ThrottleMs)
        {
            ConsoleUI.Info($"[SFX] {what}: throttled (sfx={sfxId})");
            return false;
        }
        _lastPlayTicks = now;

        uint? result = _memory.CallRemote3(t1 + TR1RMemoryMap.SoundEffect, sfxId, 0, 0);
        if (!result.HasValue)
        {
            ConsoleUI.Warning($"[SFX] {what}: remote call failed or timed out (sfx={sfxId})");
            return false;
        }

        int slot = unchecked((int)result.Value);
        ConsoleUI.Info($"[SFX] {what}: played sfx={sfxId} (slot={slot})");
        return true;
    }

    /// <summary>
    /// Verifies the live Sound_Effect prologue before letting any remote call
    /// through. Mismatch is reported once and then remembered, so a patched
    /// game degrades to silence instead of repeatedly warning.
    /// </summary>
    private bool EnsureSignature()
    {
        IntPtr t1 = _memory.Tomb1Base;
        if (t1 == IntPtr.Zero)
            return false;

        byte[] expected = TR1RMemoryMap.SoundEffectSignature;
        byte[] live = _memory.ReadBytes(t1 + TR1RMemoryMap.SoundEffect, expected.Length);
        if (live.AsSpan().SequenceEqual(expected))
            return true;

        ConsoleUI.Warning(
            $"[SFX] Sound_Effect signature MISMATCH at tomb1.dll+0x{TR1RMemoryMap.SoundEffect:X} " +
            $"(live: {BitConverter.ToString(live)}) - refusing remote calls; offsets need updating for this patch");
        return false;
    }
}
