using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectNST.GameCore.Actions;
using ProjectNST.GameCore.Board;
using ProjectNST.GameCore.Economy;
using ProjectNST.GameCore.Items;
using ProjectNST.GameCore.Randomness;
using ProjectNST.GameCore.Shop;
using ProjectNST.GameCore.Stages;

namespace ProjectNST.GameCore.Tests
{
    public sealed class GameActionTests
    {
        private static ActionId Id(string value = "trial-0/action-0") => new ActionId(value);
        private static GameActionTarget AllyTarget() =>
            GameActionTarget.FromItemUseTarget(ItemUseTarget.BoardCharacter(2));

        [Test]
        public void AllSupportedActionTypesCanBeCreatedWithStableKindsAndIds()
        {
            GameAction[] actions =
            {
                new SelectStageChoiceAction(Id(), StageChoice.SelectA),
                new BuyCharacterAction(Id("a1"), "offer-c", new GameActionLocation(CharacterLocationKind.Board, 4)),
                new BuyItemAction(Id("a2"), "offer-i", 3),
                new RerollShopAction(Id("a3"), ShopKind.Character),
                new BuyExperienceAction(Id("a4")),
                new MoveOrSwapBoardAction(Id("a5"), 0, 8),
                new MoveOrSwapHandAction(Id("a6"), 1, 4),
                new MoveBoardToHandAction(Id("a7"), 2, 0),
                new MoveHandToBoardAction(Id("a8"), 0, 2),
                new SellCharacterAction(Id("a9"), "instance-1"),
                new SellItemAction(Id("a10"), 1),
                new UseHandItemAction(Id("a11"), 1, AllyTarget()),
                new UseEventItemAction(Id("a12"), AllyTarget()),
                new EndEventAction(Id("a13")),
                new StartBattleAction(Id("a14"))
            };
            Assert.That(actions.Length, Is.EqualTo(15));
            Assert.That(actions.Select(x => x.Kind).Distinct().Count(), Is.EqualTo(15));
            Assert.That(actions.Select(x => x.ActionId.Value).Distinct().Count(), Is.EqualTo(15));
            Assert.That(Enum.GetValues(typeof(GameActionKind)).Length, Is.EqualTo(15));
        }

        [Test]
        public void ActionKindsUseExplicitReplayStableNumericValues()
        {
            Assert.That((int)GameActionKind.SelectStageChoice, Is.EqualTo(1));
            Assert.That((int)GameActionKind.UseEventItem, Is.EqualTo(13));
            Assert.That((int)GameActionKind.EndEvent, Is.EqualTo(14));
            Assert.That((int)GameActionKind.StartBattle, Is.EqualTo(15));
        }

        [Test]
        public void ActionIdIsCallerSuppliedStableAndIndependentFromPayload()
        {
            var first = new BuyExperienceAction(Id("step-7"));
            var replay = new BuyExperienceAction(Id("step-7"));
            var next = new BuyExperienceAction(Id("step-8"));
            Assert.That(first.ActionId, Is.EqualTo(replay.ActionId));
            Assert.That(first.ActionId, Is.Not.EqualTo(next.ActionId));
            Assert.That(first.Kind, Is.EqualTo(replay.Kind));
        }

        [Test]
        public void SamePayloadCanCarryDifferentActionIds()
        {
            var a = new MoveOrSwapBoardAction(Id("action-a"), 1, 5);
            var b = new MoveOrSwapBoardAction(Id("action-b"), 1, 5);
            Assert.That(a.SourceSlot, Is.EqualTo(b.SourceSlot));
            Assert.That(a.TargetSlot, Is.EqualTo(b.TargetSlot));
            Assert.That(a.ActionId, Is.Not.EqualTo(b.ActionId));
        }

        [Test]
        public void ActionAndPayloadPropertiesHaveNoPublicSetters()
        {
            Type[] actionTypes = typeof(GameAction).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(GameAction).IsAssignableFrom(t)).ToArray();
            foreach (Type type in actionTypes)
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    if (property.GetMethod != null && property.GetMethod.IsPublic)
                        Assert.That(property.SetMethod == null || !property.SetMethod.IsPublic,
                            Is.True, type.Name + "." + property.Name + " must be immutable to callers");
        }

        [Test]
        public void ActionRecordsAreSerializableAndExposeStablePrimitivePayloads()
        {
            Assert.That(Attribute.IsDefined(typeof(GameAction), typeof(SerializableAttribute)), Is.True);
            Assert.That(Attribute.IsDefined(typeof(ActionId), typeof(SerializableAttribute)), Is.True);
            Assert.That(Attribute.IsDefined(typeof(GameActionTarget), typeof(SerializableAttribute)), Is.True);
            Assert.That(Attribute.IsDefined(typeof(GameActionLocation), typeof(SerializableAttribute)), Is.True);
            Assert.That((int)new SelectStageChoiceAction(Id(), StageChoice.SelectD).Choice, Is.EqualTo((int)StageChoice.SelectD));
            Assert.That(new BuyCharacterAction(Id(), "offer-42", new GameActionLocation(CharacterLocationKind.Hand, 2)).OfferId, Is.EqualTo("offer-42"));
        }

        [Test]
        public void ActionIdRejectsEmptyOrWhitespaceValuesIncludingDefaultStruct()
        {
            Assert.Throws<ArgumentException>(() => new ActionId(null));
            Assert.Throws<ArgumentException>(() => new ActionId("  "));
            Assert.Throws<ArgumentException>(() => new BuyExperienceAction(default(ActionId)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void OfferAndInstanceIdsMustBePresent(string id)
        {
            Assert.Throws<ArgumentException>(() => new BuyCharacterAction(Id(), id,
                new GameActionLocation(CharacterLocationKind.Board, 0)));
            Assert.Throws<ArgumentException>(() => new BuyItemAction(Id(), id, 0));
            Assert.Throws<ArgumentException>(() => new SellCharacterAction(Id(), id));
        }

        [TestCase(-1)]
        [TestCase(-5)]
        public void NegativeSlotsAreRejected(int slot)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuyItemAction(Id(), "offer", slot));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SellItemAction(Id(), slot));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveOrSwapBoardAction(Id(), slot, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveOrSwapHandAction(Id(), 0, slot));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveBoardToHandAction(Id(), slot, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveHandToBoardAction(Id(), 0, slot));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UseHandItemAction(Id(), slot, AllyTarget()));
        }

        [Test]
        public void BuyPlacementRejectsInvalidKindAndNegativeIndex()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionLocation((CharacterLocationKind)8, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionLocation(CharacterLocationKind.Hand, -1));
        }

        [Test]
        public void RerollRequiresKnownNonEmptyShopKind()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RerollShopAction(Id(), ShopKind.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RerollShopAction(Id(), (ShopKind)42));
            Assert.That(new RerollShopAction(Id(), ShopKind.Item).ShopKind, Is.EqualTo(ShopKind.Item));
        }

        [Test]
        public void StageChoiceRejectsUnknownEnumValue()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SelectStageChoiceAction(Id(), (StageChoice)99));
        }

        [Test]
        public void ItemActionTargetRejectsDefaultAndMalformedTargetPayload()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UseEventItemAction(Id(), default(GameActionTarget)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionTarget((ItemUseTargetKind)99, 0, 0, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionTarget(ItemUseTargetKind.BoardCharacter, -1, 0, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionTarget(ItemUseTargetKind.EnemyArea, 0, 1, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionTarget(ItemUseTargetKind.EnemyArea, -1, -1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameActionTarget(ItemUseTargetKind.BoardArea, 0, 1, true));
        }

        [Test]
        public void ItemTargetRoundTripsToExistingItemUseTargetApi()
        {
            GameActionTarget source = GameActionTarget.FromItemUseTarget(ItemUseTarget.EnemyCharacter(4, false));
            ItemUseTarget restored = source.ToItemUseTarget();
            Assert.That(restored.Kind, Is.EqualTo(source.Kind));
            Assert.That(restored.SlotIndex, Is.EqualTo(4));
            Assert.That(restored.IsPresent, Is.False);
        }

        [Test]
        public void ActionResultDistinguishesSuccessRejectionAndInternalError()
        {
            ActionId id = Id("result-1");
            ActionResult success = ActionResult.Success(id);
            ActionResult rejected = ActionResult.Reject(id, ActionErrorCode.InsufficientGold, "not enough gold");
            ActionResult error = ActionResult.Fail(id, ActionErrorCode.ConfigurationError, "missing rule config");
            Assert.That(success.Status, Is.EqualTo(ActionResultStatus.Succeeded));
            Assert.That(success.Succeeded, Is.True);
            Assert.That(success.Error.IsNone, Is.True);
            Assert.That(rejected.Status, Is.EqualTo(ActionResultStatus.Rejected));
            Assert.That(rejected.Error.Code, Is.EqualTo(ActionErrorCode.InsufficientGold));
            Assert.That(rejected.Error.Detail, Is.EqualTo("not enough gold"));
            Assert.That(error.Status, Is.EqualTo(ActionResultStatus.Error));
            Assert.That(error.Error.Code, Is.EqualTo(ActionErrorCode.ConfigurationError));
        }

        [Test]
        public void ResultRejectsMissingIdentityAndInvalidFailureKinds()
        {
            Assert.Throws<ArgumentException>(() => ActionResult.Success(default(ActionId)));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionResult.Reject(Id(), ActionErrorCode.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionResult.Fail(Id(), ActionErrorCode.InsufficientGold));
            Assert.That(ActionResult.Fail(Id(), ActionErrorCode.InternalError).Status,
                Is.EqualTo(ActionResultStatus.Error));
        }

        [Test]
        public void StructuralActionCreationDoesNotMutateGameStateOrConsumeRng()
        {
            var economy = new EconomyState(15, 2, 2, 12, 15, 0, 1, 20, 30);
            BoardState board = BoardState.CreateStandard();
            HandState hand = HandState.CreateStandard();
            StageProgressState stage = StageProgressRules.CreateInitialState(
                new StageProgressConfig(25, 5, 5, 4, 0, 0, 0, 1, 0, 0, 3, 6, 0, 0, 10, 4, 2, true));
            var streams = new SimulationRandomStreams(0x12345678UL);
            SimulationRandomStreamsState before = streams.CaptureState();

            GameAction[] actions =
            {
                new SelectStageChoiceAction(Id(), StageChoice.SelectB),
                new BuyCharacterAction(Id("b"), "offer", new GameActionLocation(CharacterLocationKind.Hand, 0)),
                new BuyItemAction(Id("c"), "item-offer", 1),
                new RerollShopAction(Id("d"), ShopKind.Character),
                new BuyExperienceAction(Id("e")),
                new MoveOrSwapBoardAction(Id("f"), 0, 1),
                new MoveOrSwapHandAction(Id("g"), 0, 1),
                new MoveBoardToHandAction(Id("h"), 0, 0),
                new MoveHandToBoardAction(Id("i"), 0, 0),
                new SellCharacterAction(Id("j"), "instance"),
                new SellItemAction(Id("k"), 0),
                new UseHandItemAction(Id("l"), 0, AllyTarget()),
                new UseEventItemAction(Id("m"), AllyTarget()),
                new EndEventAction(Id("n")),
                new StartBattleAction(Id("o"))
            };

            Assert.That(actions.Length, Is.EqualTo(15));
            Assert.That(economy.Gold, Is.EqualTo(15));
            Assert.That(economy.XP, Is.EqualTo(2));
            Assert.That(board.SlotCount, Is.EqualTo(9));
            Assert.That(hand.EmptySlotCount, Is.EqualTo(5));
            Assert.That(stage.EncounterIndex, Is.EqualTo(0));
            AssertStreamsEqual(before, streams.CaptureState());
        }

        private static void AssertStreamsEqual(SimulationRandomStreamsState expected, SimulationRandomStreamsState actual)
        {
            Assert.That(actual.TrialSeed, Is.EqualTo(expected.TrialSeed));
            Assert.That(actual.Version, Is.EqualTo(expected.Version));
            Assert.That(actual.Shop, Is.EqualTo(expected.Shop));
            Assert.That(actual.Stats, Is.EqualTo(expected.Stats));
            Assert.That(actual.Encounter, Is.EqualTo(expected.Encounter));
            Assert.That(actual.Battle, Is.EqualTo(expected.Battle));
            Assert.That(actual.Event, Is.EqualTo(expected.Event));
            Assert.That(actual.Roster, Is.EqualTo(expected.Roster));
            Assert.That(actual.Economy, Is.EqualTo(expected.Economy));
        }
    }
}

