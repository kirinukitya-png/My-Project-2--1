using NUnit.Framework;
using ProjectNST.GameCore.Sessions;
using ProjectNST.GameCore.Randomness;

namespace ProjectNST.GameCore.Tests
{
    public sealed class TrialIndexSelectionParserTests
    {
        [Test] public void ParsesInclusiveRangesAndKeepsFirstOccurrenceOrder()
        {
            Assert.That(TrialIndexSelectionParser.TryParse("0-2, 217, 2, 900-901",out var values,out var error),Is.True,error);
            Assert.That(values,Is.EqualTo(new long[]{0,1,2,217,900,901}));
        }
        [TestCase("")][TestCase("-1")][TestCase("3-")][TestCase("5-2")][TestCase("1,,2")][TestCase("x")]
        public void RejectsMalformedAndNegativeInput(string input)
        { Assert.That(TrialIndexSelectionParser.TryParse(input,out var values,out var error),Is.False); Assert.That(values,Is.Empty); Assert.That(error,Is.Not.Empty); }
        [Test] public void DirectTrialSeedIsSameSeedAsBatchIndex()
        { Assert.That(TrialSeedGenerator.CreateTrialSeed(12345UL,906UL),Is.EqualTo(TrialSeedGenerator.Create(12345UL,906UL).TrialSeed)); }
    }
}
