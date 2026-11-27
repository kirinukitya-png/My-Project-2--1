using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Strategy;
using ProjectNST.SimulationExport;

namespace ProjectNST.GameCore.Tests
{
    public sealed class DecisionTraceCaptureTests
    {
        private string _directory;
        [SetUp]
        public void CreateDirectory()
        {
            _directory = Path.Combine(Path.GetTempPath(), "NST-trace-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void DeleteOutputs()
        {
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }

        private DecisionTraceCapture Capture(bool requested = true, bool full = true, bool supported = true,
            long trial = 13, Action<string, string> writer = null) =>
            new DecisionTraceCapture(_directory, "HeuristicStrategy", "14", trial, 12345, requested, full, supported, writer);

        [TestCase(false, true, true, "NotRequested", false)]
        [TestCase(true, false, true, "DisabledByFullDiagnostics", true)]
        [TestCase(true, true, false, "UnsupportedStrategy", true)]
        public void DisabledCaptureDoesNotRegister(bool requested, bool full, bool supported, string state, bool warn)
        {
            var capture = Capture(requested, full, supported);
            bool called = false;
            capture.Register((write, error) => called = true);
            capture.Complete(2);
            Assert.That(called, Is.False);
            Assert.That(capture.TargetDecisions, Is.Null);
            Assert.That(capture.FileExists, Is.Null);
            Assert.That(capture.Status, Is.EqualTo(state));
            DecisionTraceCapture.BatchSummary(new[] { capture }, out bool actual);
            Assert.That(actual, Is.EqualTo(warn));
        }

        [Test]
        public void RegistrationFailureIsSeparateFromNoDecisions()
        {
            var capture = Capture();
            capture.Register((write, error) => throw new InvalidOperationException("attach"));
            capture.Complete(0);
            Assert.That(capture.Status, Is.EqualTo("RegistrationFailed"));
            Assert.That(capture.FirstError, Does.Contain("Registration").And.Contain("attach"));
            Assert.That(capture.RegistrationFailures, Is.EqualTo(1));
        }

        [TestCase(0, 0, "NoDecisions")]
        [TestCase(1, 0, "MissingFile")]
        [TestCase(2, 1, "Partial")]
        [TestCase(2, 2, "Saved")]
        public void CompletenessUsesDecisionCountAndSavedBlocks(int decisions, int writes, string expected)
        {
            var capture = Capture();
            capture.Register((write, error) => { for (int i = 0; i < writes; i++) write("decision " + i); });
            capture.Complete(decisions);
            Assert.That(capture.TargetDecisions, Is.EqualTo(decisions));
            Assert.That(capture.Status, Is.EqualTo(expected));
            Assert.That(capture.Written, Is.EqualTo(writes));
            var summary = DecisionTraceCapture.BatchSummary(new[] { capture }, out bool warning);
            Assert.That(warning, Is.EqualTo(writes != decisions));
            Assert.That(summary, Does.Contain("DecisionTraceEffectiveTrials=1"));
        }

        [Test]
        public void EmptyAndTruncatedOutputsAreDetectedEvenWhenWriterReturns()
        {
            var empty = Capture(writer: (path, text) => File.WriteAllText(path, ""));
            empty.Register((write, _) => write("lost"));
            empty.Complete(1);
            Assert.That(empty.Status, Is.EqualTo("EmptyFile"));
            var truncated = Capture(trial: 17, writer: (path, text) => File.WriteAllText(path, "x"));
            truncated.Register((write, _) => write("also lost"));
            truncated.Complete(1);
            Assert.That(truncated.Status, Is.EqualTo("Partial"));
        }

        [Test]
        public void GenerationAndWriteFailuresRetainSeparateCountsAndFirstError()
        {
            var capture = Capture(writer: (path, text) => throw new IOException("disk"));
            capture.Register((write, failure) =>
            {
                failure(false, new InvalidOperationException("format"));
                write("formatted");
            });
            capture.Complete(2);
            Assert.That(capture.Generated, Is.EqualTo(1));
            Assert.That(capture.GenerationFailures, Is.EqualTo(1));
            Assert.That(capture.WriteFailures, Is.EqualTo(1));
            Assert.That(capture.FirstError, Does.Contain("format").And.Not.Contain("disk"));
            var summary = DecisionTraceCapture.BatchSummary(new[] { capture }, out bool warning);
            Assert.That(warning, Is.True);
            Assert.That(summary, Does.Contain("DecisionTraceErrors=2").And.Contain("DecisionTraceFailedTrials=1"));
        }

        [Test]
        public void TrialFilesAndCountersDoNotMix()
        {
            var first = Capture();
            var second = Capture(trial: 17);
            first.Register((write, _) => write("first-only"));
            second.Register((write, _) => write("second-only"));
            first.Complete(1); second.Complete(1);
            Assert.That(File.ReadAllText(Path.Combine(_directory, second.FileName)), Does.Not.Contain("first-only"));
            Assert.That(first.Written, Is.EqualTo(1));
            Assert.That(second.Written, Is.EqualTo(1));
        }
    }

    public sealed partial class HeuristicStrategyTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void CandidateTraceSinkFailureDoesNotChangeDecisionStateOrRng(bool v14)
        {
            var clean = v14 ? HeuristicStrategy.CreateV14() : HeuristicStrategy.CreateV13();
            var traced = v14 ? HeuristicStrategy.CreateV14() : HeuristicStrategy.CreateV13();
            var o = V14Observation(gold: 11, level: 9);
            var rng = StrategyRandomSource.Create(123, clean.StrategyId, clean.Version).CaptureState();
            var baseline = clean.ChooseAction(o, Context(clean, rng: rng));
            int observedErrors = 0;
            traced.ConfigureDecisionTrace(_ => throw new IOException("trace"), (sink, error) =>
            {
                if (sink && error is IOException) observedErrors++;
            });
            var actual = traced.ChooseAction(o, Context(traced, rng: rng));
            Assert.That(ProjectNST.GameCore.Sessions.SimulationStateFingerprint.Create(actual.Action),
                Is.EqualTo(ProjectNST.GameCore.Sessions.SimulationStateFingerprint.Create(baseline.Action)));
            Assert.That(actual.NextState.Payload, Is.EqualTo(baseline.NextState.Payload));
            Assert.That(actual.NextStrategyRngState, Is.EqualTo(baseline.NextStrategyRngState));
            Assert.That(actual.StrategyRngAdvanced, Is.EqualTo(baseline.StrategyRngAdvanced));
            Assert.That(observedErrors, Is.EqualTo(1));
        }

        [Test]
        public void V14TraceDistinguishesUncomputedGoldGateAndEarlyLimit()
        {
            string text = null;
            var strategy = HeuristicStrategy.CreateV14(decisionDiagnosticSink: trace => text = trace);
            var o = V14Observation(gold: 11, level: 9);
            strategy.ChooseAction(o, Context(strategy));
            Assert.That(text, Does.Contain("RerollGeneration=Generated").And.Contain("status=LowGoldReroll score=Unavailable")
                .And.Contain("ShopEvaluation calls=0"));
            o = V14Observation(gold: 20, level: 9, choice: ProjectNST.GameCore.Stages.StageChoice.SelectD);
            var limited = new StrategyState(strategy.StrategyId, strategy.Version, 2, "hs2|29|0|2|");
            strategy.ChooseAction(o, Context(strategy, state: limited));
            Assert.That(text, Does.Contain("RerollGeneration=NotRun").And.Contain("ShopEvaluation calls=0"));
        }
    }
}
