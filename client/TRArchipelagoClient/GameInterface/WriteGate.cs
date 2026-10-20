using TRArchipelagoClient.UI;

namespace TRArchipelagoClient.GameInterface;

/// <summary>
/// Write-verification gate. Every offset-group memory write should go through
/// CheckedWrite so a stale offset is caught at the moment it stops working:
///
///   write -> read back -> compare
///       mismatch => report "VERSION DIFF DETECTED" for the owning group
///                => recalculate the group's offsets
///                => retry (max 3 attempts)
///                => quarantine the group when recalculation keeps failing
///
/// Quarantine is sticky: further writes for that group are refused until the
/// next level transition forces a re-identification pass.
/// </summary>
public static class WriteGate
{
    public const int MaxAttempts = 3;

    private static readonly HashSet<string> _quarantineNotices = new();

    /// <summary>
    /// Writes <paramref name="value"/> and confirms it landed where intended.
    /// Returns false when the write could not be validated (group quarantined).
    /// </summary>
    public static bool CheckedWrite<T>(ProcessMemory m, OffsetGroup group, IntPtr address, T value, string operation)
        where T : struct, IEquatable<T>
    {
        if (OffsetModel.IsQuarantined(group))
        {
            NotifyOnce(group, operation);
            return false;
        }

        string lastReason = "unknown";
        IntPtr t1 = m.Tomb1Base;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Pending groups (not yet identified) are still allowed through:
            // the read-back below is what decides whether the offset is stale.
            bool writeOk = m.Write(address, value);

            if (writeOk)
            {
                T readBack = m.Read<T>(address);
                if (readBack.Equals(value))
                {
                    return true;
                }

                lastReason = $"read-back mismatch at 0x{(address.ToInt64() - t1.ToInt64()):X}: wrote {value}, read {readBack}";
            }
            else
            {
                lastReason = $"WriteProcessMemory refused 0x{(address.ToInt64() - t1.ToInt64()):X}";
            }

            ConsoleUI.Warning($"[GATE] {group}/{operation} attempt {attempt}/{MaxAttempts}: {lastReason}");
            OffsetIdentifier.ReportVersionDiff(group, new[] { lastReason });

            if (attempt < MaxAttempts && !Recalculate(m, group))
            {
                NotifyOnce(group, operation);
                return false;
            }
        }

        OffsetIdentifier.Quarantine(group, $"{operation}: {lastReason}");
        NotifyOnce(group, operation);
        return false;
    }

    /// <summary>
    /// Validates the group against the live game and, where a scanner exists,
    /// re-derives it. Only the Ring group currently has a resolver.
    /// </summary>
    private static bool Recalculate(ProcessMemory m, OffsetGroup group)
    {
        if (group == OffsetGroup.Ring)
            return OffsetIdentifier.IdentifyRing(m, forceResolve: true);

        var failures = OffsetModel.Validate(m, group);
        if (failures.Count == 0)
        {
            OffsetModel.Report(group).Reset(OffsetState.Verified);
            return true;
        }

        OffsetModel.Report(group).LastError = failures[0];
        return false;
    }

    private static void NotifyOnce(OffsetGroup group, string operation)
    {
        if (!_quarantineNotices.Add($"{group}/{operation}")) return;

        ConsoleUI.Error(
            $"[GATE] {operation} disabled: {group} offsets are quarantined " +
            $"({OffsetModel.Report(group).LastError ?? "recalculation failed"}).");
        ConsoleUI.Error("[GATE] The group is re-checked on the next level transition.");
    }
}
