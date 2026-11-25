using System;
using System.Collections.Generic;
using System.Linq;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Sessions;

/// <summary>Pure formatting/selection helpers for Editor-only stopped-Trial diagnostics.</summary>
internal static class NSTSimulationRejectionDiagnostics
{
    internal static bool IsDiagnosticStopReason(SimulationStopReason reason) =>
        reason == SimulationStopReason.NoActionNotAllowed ||
        reason == SimulationStopReason.DecisionLimitExceeded ||
        reason == SimulationStopReason.EventItemSelectionRequired ||
        reason == SimulationStopReason.ConsecutiveRejectionLimitExceeded;

    internal static IReadOnlyList<SimulationExecutionRecord> GetLastActionRecords(
        IEnumerable<SimulationExecutionRecord> history, int maximumCount)
    {
        if (history == null) throw new ArgumentNullException(nameof(history));
        if (maximumCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        return history.Where(x => x != null && x.Kind == SimulationExecutionEventKind.Action)
            .OrderBy(x => x.Sequence).TakeLast(maximumCount).ToArray();
    }

    internal static IReadOnlyList<SimulationExecutionRecord> GetTrailingRejectionRecords(
        IEnumerable<SimulationExecutionRecord> history, int expectedCount)
    {
        if (history == null) throw new ArgumentNullException(nameof(history));
        if (expectedCount < 0) throw new ArgumentOutOfRangeException(nameof(expectedCount));
        if (expectedCount == 0) return Array.Empty<SimulationExecutionRecord>();
        SimulationExecutionRecord[] ordered = history.Where(x => x != null &&
                x.Kind != SimulationExecutionEventKind.TrialEnd)
            .OrderBy(x => x.Sequence).ToArray();
        var trailing = new List<SimulationExecutionRecord>(expectedCount);
        for (int i = ordered.Length - 1; i >= 0 && trailing.Count < expectedCount; i--)
        {
            SimulationExecutionRecord item = ordered[i];
            if (item.Kind != SimulationExecutionEventKind.Action ||
                item.ActionStatus != ActionDispatchStatus.Rejected) break;
            trailing.Add(item);
        }
        trailing.Reverse();
        return trailing.AsReadOnly();
    }

}
