using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ProjectNST.SimulationExport
{
    /// <summary>Records diagnostic delivery independently of the game result, for one Trial only.</summary>
    public sealed class DecisionTraceCapture
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private readonly string _path;
        private readonly Action<string, string> _append;
        private long _writtenBytes;
        public string StrategyId { get; }
        public string StrategyVersion { get; }
        public long TrialIndex { get; }
        public ulong TrialSeed { get; }
        public bool Requested { get; }
        public bool FullDiagnostics { get; }
        public bool Supported { get; }
        public bool Effective => Requested && FullDiagnostics && Supported && Registered;
        public bool Registered { get; private set; }
        public long? TargetDecisions { get; private set; }
        public int Generated => Callbacks;
        public int GenerationFailures { get; private set; }
        public int Callbacks { get; private set; }
        public int Written { get; private set; }
        public int WriteFailures { get; private set; }
        public int RegistrationFailures { get; private set; }
        public int VerificationFailures { get; private set; }
        public string FirstError { get; private set; }
        public string FileName => Path.GetFileName(_path);
        public bool? FileExists { get; private set; }
        public long? FileBytes { get; private set; }
        public int ErrorCount => RegistrationFailures + GenerationFailures + WriteFailures + VerificationFailures;
        public string Status { get; private set; }

        public DecisionTraceCapture(string directory, string strategyId, string strategyVersion,
            long trialIndex, ulong trialSeed, bool requested, bool fullDiagnostics, bool supported,
            Action<string, string> append = null)
        {
            StrategyId = strategyId;
            StrategyVersion = strategyVersion;
            TrialIndex = trialIndex;
            TrialSeed = trialSeed;
            Requested = requested;
            FullDiagnostics = fullDiagnostics;
            Supported = supported;
            _path = Path.Combine(directory, $"{strategyId}_v{strategyVersion}_trial_{trialIndex.ToString(CultureInfo.InvariantCulture)}_decision_trace.txt");
            _append = append ?? ((path, content) => File.AppendAllText(path, content, Utf8));
        }

        public bool NeedsRegistration => Requested && FullDiagnostics && Supported;

        public void Register(Action<Action<string>, Action<bool, Exception>> attach)
        {
            if (!NeedsRegistration) return;
            try
            {
                attach(Write, ReportPublisherError);
                Registered = true;
            }
            catch (Exception error)
            {
                RegistrationFailures++;
                Remember("Registration", error);
            }
        }

        private void Write(string trace)
        {
            Callbacks++;
            try
            {
                string block = trace + Environment.NewLine + "--- decision ---" + Environment.NewLine;
                _append(_path, block);
                _writtenBytes += Utf8.GetByteCount(block);
                Written++;
            }
            catch (Exception error)
            {
                WriteFailures++;
                Remember("Write", error);
            }
        }

        private void ReportPublisherError(bool sinkFailure, Exception error)
        {
            if (sinkFailure) WriteFailures++;
            else GenerationFailures++;
            Remember(sinkFailure ? "Callback" : "Generation", error);
        }

        private void Remember(string phase, Exception error)
        {
            if (FirstError != null) return;
            string message = phase + ": " + error.GetType().Name + ": " + error.Message;
            FirstError = message.Length <= 1024 ? message : message.Substring(0, 1024);
        }

        public void Complete(long decisions)
        {
            if (Effective) TargetDecisions = decisions;
            if (!Requested) { Status = "NotRequested"; return; }
            if (!FullDiagnostics) { Status = "DisabledByFullDiagnostics"; return; }
            if (!Supported) { Status = "UnsupportedStrategy"; return; }
            if (!Registered) { Status = "RegistrationFailed"; return; }
            try
            {
                var file = new FileInfo(_path);
                FileExists = file.Exists;
                if (FileExists.Value) FileBytes = file.Length;
            }
            catch (Exception error)
            {
                VerificationFailures++;
                Remember("Verify", error);
                Status = "VerificationFailed";
                return;
            }

            if (decisions == 0 && ErrorCount == 0 && Callbacks == 0) Status = "NoDecisions";
            else if (FileExists == false) Status = "MissingFile";
            else if (FileBytes == 0) Status = "EmptyFile";
            else if (Written == 0) Status = "AllDecisionsMissing";
            else if (Written != decisions || FileBytes != _writtenBytes || ErrorCount != 0) Status = "Partial";
            else Status = "Saved";
        }

        public static string CsvHeader =>
            "StrategyId,StrategyVersion,TrialIndex,TrialSeed,Requested,Effective,Registration,TargetDecisions,Generated,GenerationFailures,Callbacks,Written,WriteFailures,TraceFile,FileExists,FileBytes,FirstError,Status";

        public string ToCsv() => string.Join(",", new object[] {
            StrategyId, StrategyVersion, TrialIndex, TrialSeed, Requested, Effective,
            Registered ? "Registered" : NeedsRegistration ? "Failed" : "NotApplicable",
            TargetDecisions, Effective ? (int?)Generated : null, Effective ? (int?)GenerationFailures : null,
            Effective ? (int?)Callbacks : null, Effective ? (int?)Written : null,
            Effective ? (int?)WriteFailures : null, NeedsRegistration ? FileName : null, FileExists, FileBytes,
            FirstError, Status
        }.Select(x => "\"" + (Convert.ToString(x, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\""));

        public static string BatchSummary(IReadOnlyList<DecisionTraceCapture> captures, out bool warning)
        {
            int requested = captures.Count(x => x.Requested);
            int effective = captures.Count(x => x.Effective);
            int saved = captures.Count(x => x.Status == "Saved");
            int partial = captures.Count(x => x.Status == "Partial");
            int noDecisions = captures.Count(x => x.Status == "NoDecisions");
            int failed = requested - saved - partial - noDecisions;
            int allMissing = captures.Count(x => x.TargetDecisions > 0 && x.Written == 0);
            warning = failed > 0 || partial > 0;
            return FormattableString.Invariant(
                $"DecisionTraceRequestedTrials={requested}\nDecisionTraceEffectiveTrials={effective}\nDecisionTraceSavedTrials={saved}\nDecisionTracePartialTrials={partial}\nDecisionTraceFailedTrials={failed}\nDecisionTraceAllMissingTrials={allMissing}\nDecisionTraceNoDecisionTrials={noDecisions}\nDecisionTraceFiles={captures.Count(x => x.FileExists == true)}\nDecisionTraceGeneratedBlocks={captures.Sum(x => (long)x.Generated)}\nDecisionTraceWrittenBlocks={captures.Sum(x => (long)x.Written)}\nDecisionTraceErrors={captures.Sum(x => (long)x.ErrorCount)}\nDecisionTraceWarning={warning}\n");
        }
    }
}
