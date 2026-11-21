using System;
using System.Collections.Generic;
using System.Linq;
using Game.Content;
using Game.Contracts;
using Game.Core;

// 在项目根目录运行 dotnet run --project Tests/Game.Battle.Tests 即可执行这些规则测试。
var tests = new Action[]
{
    CardDefinitionsKeepTheirEffects,
    QueueEditsAreBoundedAndDoNotResolve,
    EndTurnResolvesEveryOccupiedSlotInOrder,
    RepeatedBenefitUsesOneSlotAndReportsEachGrant,
    BenefitScalesWithOccupiedSlotsOnBothSides,
    BenefitUsesActualDrawsInEffectOrderAndResetsEachTurn,
    DrawEffectsKeepQueuedCardsOutOfTheDeck,
    EmptyTurnsCountTowardsTheDeadline,
    TheExactTargetWinsOnTheLastTurn,
    ReachingTheTargetAllowsAnOptionalEarlyFinish,
    ScenariosShareRulesAndShuffleDeterministically
};

foreach (var test in tests)
{
    test();
    Console.WriteLine($"PASS {test.Method.Name}");
}

Console.WriteLine($"Passed {tests.Length} battle tests.");

static void CardDefinitionsKeepTheirEffects()
{
    var gain = new CardEffect(CardEffectKind.GainBenefit, 4);
    var sourceEffects = new List<CardEffect> { gain };
    var card = new CardDefinition("gain", "Gain", sourceEffects);

    sourceEffects.Clear();

    var effect = card.Effects.Single();
    Equal(CardEffectKind.GainBenefit, effect.Kind);
    Equal(4, effect.Amount);
}

static void QueueEditsAreBoundedAndDoNotResolve()
{
    var gain = Card("gain", CardEffectKind.GainBenefit, 3);
    var session = Start(Enumerable.Repeat(gain, 6), turns: 2, target: 15, draw: 6);
    var initial = session.Snapshot;
    var cards = initial.Hand;
    Equal(6, cards.Select(card => card.InstanceId).Distinct().Count());
    Equal(5, initial.Queue.Count);

    Reject(session, new PlaceCard(cards[0].InstanceId, -1), BattleError.InvalidSlot);
    Reject(session, new PlaceCard(cards[0].InstanceId, 5), BattleError.InvalidSlot);
    Reject(session, new PlaceCard(99, 0), BattleError.CardUnavailable);
    for (int index = 0; index < 5; index++)
    {
        Submit(session, new PlaceCard(cards[index].InstanceId, index));
    }

    Equal(0L, session.Snapshot.Benefit);
    Equal(1, session.Snapshot.Hand.Count);
    Reject(session, new PlaceCard(cards[5].InstanceId, 0), BattleError.SlotOccupied);
    Reject(session, new PlaceCard(cards[0].InstanceId, 3), BattleError.SlotOccupied);
    Reject(session, new ReturnCard(cards[5].InstanceId), BattleError.CardUnavailable);
    var stale = session.Execute(new EndTurn(), initial.Revision);
    Equal(BattleError.StaleSnapshot, stale.Error);

    Submit(session, new ReturnCard(cards[2].InstanceId));
    Submit(session, new PlaceCard(cards[0].InstanceId, 2));
    Equal(0L, session.Snapshot.Benefit);
    Equal(null, session.Snapshot.Queue[0]);
    Equal(cards[0].InstanceId, session.Snapshot.Queue[2].InstanceId);
    Equal(2, session.Snapshot.Hand.Count);
    Equal(6, initial.Hand.Count);
    Equal(true, initial.Queue.All(card => card == null));
}

static void EndTurnResolvesEveryOccupiedSlotInOrder()
{
    var funding = Card("funding", CardEffectKind.GainBenefit, 9);
    var collaboration = new CardDefinition("collaboration", "Collaboration", new[]
    {
        new CardEffect(CardEffectKind.GainBenefitPerEarlierCard, 5),
        new CardEffect(CardEffectKind.GainBenefit, 2)
    });
    var session = Start(new[] { funding, collaboration }, turns: 1, target: 8, draw: 2);

    // 先放右侧牌、再放左侧牌。左侧牌单独就足以达标，但右侧仍需结算。
    Place(session, "collaboration", 4);
    Place(session, "funding", 1);
    int beforeEnd = session.Snapshot.Revision;

    Submit(session, new EndTurn());

    var settled = session.Snapshot;
    Equal(1, settled.Turn);
    Equal(1, settled.LastResolvedTurn);
    Equal(BattlePhase.Won, settled.Phase);
    Equal(16L, settled.Benefit);
    Equal("1,4", string.Join(",", settled.LastResolutions.Select(entry => entry.SlotIndex)));
    Equal("9,7", string.Join(",", settled.LastResolutions.Select(entry => entry.BenefitGained)));
    Equal(true, settled.Queue.All(card => card == null));
    Equal(2, settled.DiscardCount);
    Equal(BattleError.StaleSnapshot, session.Execute(new EndTurn(), beforeEnd).Error);
    Reject(session, new EndTurn(), BattleError.ChallengeEnded);
}

static void RepeatedBenefitUsesOneSlotAndReportsEachGrant()
{
    var surveys = new CardDefinition("surveys", "Surveys", new[]
    {
        new CardEffect(CardEffectKind.GainBenefit, 7, repeatCount: 3)
    });
    var session = Start(new[] { surveys }, turns: 1, target: 15, draw: 1);
    Place(session, "surveys", 3);
    Equal(1, session.Snapshot.Queue.Count(card => card != null));
    Equal(0L, session.Snapshot.Benefit);
    Submit(session, new EndTurn());

    var result = session.Snapshot.LastResolutions.Single();
    var award = result.EffectResolutions.Single();
    Equal(3, result.SlotIndex);
    Equal(7L, award.BenefitPerGrant);
    Equal(3, award.BenefitGrantCount);
    Equal(21L, award.BenefitGained);
    Equal(21L, result.BenefitGained);
    Equal(21L, session.Snapshot.Benefit);
    Equal(BattlePhase.Won, session.Snapshot.Phase);
}

static void BenefitScalesWithOccupiedSlotsOnBothSides()
{
    var linkage = new CardDefinition("linkage", "Linkage", new[]
    {
        new CardEffect(CardEffectKind.GainBenefitPerEarlierCard, 2),
        new CardEffect(CardEffectKind.GainBenefitPerLaterCard, 4)
    });
    var work = Card("work", CardEffectKind.GainBenefit, 1);
    var session = Start(new[] { linkage, work, work }, turns: 1, target: 8, draw: 3);
    Place(session, "work", 4);
    Place(session, "linkage", 2);
    Place(session, "work", 0);
    Submit(session, new EndTurn());

    // 结算时两侧各有一张牌，空着的槽位以及拖入的先后都不参与计数。
    var result = session.Snapshot.LastResolutions[1];
    Equal(6L, result.BenefitGained);
    Equal("2,4", string.Join(",", result.EffectResolutions.Select(effect => effect.BenefitGained)));
    Equal(8L, session.Snapshot.Benefit);
}

static void BenefitUsesActualDrawsInEffectOrderAndResetsEachTurn()
{
    var readDraws = new CardEffect(CardEffectKind.GainBenefitPerDrawnCard, 3);
    var process = new CardDefinition("process", "Process", new[]
    {
        readDraws,
        new CardEffect(CardEffectKind.Draw, 4),
        readDraws
    });
    var followUp = Card("follow_up", CardEffectKind.GainBenefitPerDrawnCard, 5);
    var spare = Card("spare", CardEffectKind.GainBenefit, 1);
    var session = Start(new[] { process, followUp, spare }, turns: 2, target: 20, draw: 3);
    Place(session, "process", 0);
    Place(session, "follow_up", 4);
    Submit(session, new EndTurn());

    // 这次只有未排入队列的那一张可抽，请求抽四张实际只抽到一张。
    var firstTurn = session.Snapshot;
    var effects = firstTurn.LastResolutions[0].EffectResolutions;
    Equal("0,0,3", string.Join(",", effects.Select(effect => effect.BenefitGained)));
    Equal("0,1,0", string.Join(",", effects.Select(effect => effect.CardsDrawn)));
    Equal(5L, firstTurn.LastResolutions[1].BenefitGained);
    Equal(8L, firstTurn.Benefit);

    Place(session, "follow_up", 0);
    Submit(session, new EndTurn());
    Equal(0L, session.Snapshot.LastResolutions.Single().BenefitGained);
    Equal(8L, session.Snapshot.Benefit);
    Equal(3, effects.Count);
}

static void DrawEffectsKeepQueuedCardsOutOfTheDeck()
{
    var drawCard = Card("research", CardEffectKind.Draw, 4);
    var gain = Card("funding", CardEffectKind.GainBenefit, 1);
    var session = Start(new[] { drawCard, gain, gain }, turns: 2, target: 10, draw: 3);
    Place(session, "research", 0);
    Place(session, "funding", 2);
    var unqueued = session.Snapshot.Hand.Single();
    int queuedDrawId = session.Snapshot.Queue[0].InstanceId;
    int queuedGainId = session.Snapshot.Queue[2].InstanceId;
    Submit(session, new EndTurn());

    // 结算抽牌时只有未入队的那一张可抽。随后回合开始抽牌，才会补回已结算的牌。
    var next = session.Snapshot;
    Equal(1, next.LastResolutions[0].CardsDrawn);
    Equal(unqueued.InstanceId, next.Hand[0].InstanceId);
    Equal(3, next.Hand.Count);
    Equal(true, next.Hand.Any(card => card.InstanceId == queuedDrawId));
    Equal(true, next.Hand.Any(card => card.InstanceId == queuedGainId));
    Equal(3, next.Hand.Count + next.DrawCount + next.DiscardCount);
}

static void EmptyTurnsCountTowardsTheDeadline()
{
    var session = Start(new[] { Card("gain", CardEffectKind.GainBenefit, 1) },
        turns: 2, target: 2, draw: 1);
    Submit(session, new EndTurn());
    Equal(BattlePhase.Planning, session.Snapshot.Phase);
    Equal(2, session.Snapshot.Turn);
    Equal(1, session.Snapshot.Hand.Count);
    Place(session, "gain", 0);
    Submit(session, new EndTurn());
    Equal(BattlePhase.Lost, session.Snapshot.Phase);
    Equal(1L, session.Snapshot.Benefit);
    Equal(2, session.Snapshot.Turn);
    Equal(0, session.Snapshot.Hand.Count);
}

static void TheExactTargetWinsOnTheLastTurn()
{
    var gain = Card("gain", CardEffectKind.GainBenefit, 3);
    var session = Start(Enumerable.Repeat(gain, 5), turns: 2, target: 18, draw: 5);
    Place(session, "gain", 2);
    var beforeFirstEnd = session.Snapshot;
    Submit(session, new EndTurn());

    var firstSettlement = session.Snapshot;
    Equal(BattlePhase.Planning, firstSettlement.Phase);
    Equal(3L, firstSettlement.Benefit);

    // 模拟结束按钮使用同一个界面版本提交两次，第二次不得多结算一个回合。
    var duplicate = session.Execute(new EndTurn(), beforeFirstEnd.Revision);
    Equal(BattleError.StaleSnapshot, duplicate.Error);
    Equal(firstSettlement, duplicate.Snapshot);
    Equal(firstSettlement, session.Snapshot);

    for (int slot = 0; slot < BattleSnapshot.QueueCapacity; slot++)
    {
        Place(session, "gain", slot);
    }
    var beforeFinalEnd = session.Snapshot;
    Submit(session, new EndTurn());

    var final = session.Snapshot;
    Equal(BattlePhase.Won, final.Phase);
    Equal(2, final.LastResolvedTurn);
    Equal(18L, final.Benefit);
    Equal(5, final.LastResolutions.Count);
    Equal(true, final.Queue.All(card => card == null));
    Equal(5, beforeFinalEnd.Queue.Count(card => card != null));
    Equal(1, firstSettlement.LastResolutions.Count);
    Equal(2, firstSettlement.LastResolutions[0].SlotIndex);
}

static void ReachingTheTargetAllowsAnOptionalEarlyFinish()
{
    var session = Start(new[] { Card("gain", CardEffectKind.GainBenefit, 3) },
        turns: 3, target: 3, draw: 1);
    Reject(session, new FinishChallenge(), BattleError.TargetNotMet);
    Place(session, "gain", 4);
    Submit(session, new EndTurn());
    Equal(BattlePhase.Planning, session.Snapshot.Phase);
    Equal(true, session.Snapshot.CanFinishEarly);

    Place(session, "gain", 3);
    Equal(false, session.Snapshot.CanFinishEarly);
    Reject(session, new FinishChallenge(), BattleError.QueueNotEmpty);
    Submit(session, new EndTurn());
    Equal(6L, session.Snapshot.Benefit);
    Equal(true, session.Snapshot.CanFinishEarly);
    Submit(session, new FinishChallenge());
    Equal(BattlePhase.Won, session.Snapshot.Phase);
    Equal(3, session.Snapshot.Turn);
    Equal(2, session.Snapshot.LastResolvedTurn);
}

static void ScenariosShareRulesAndShuffleDeterministically()
{
    Equal("设施,保护区,博物馆,子公司",
        string.Join(",", BattleDemoContent.Scenarios.Select(scenario => scenario.FacilityName)));
    var sessions = BattleDemoContent.Scenarios
        .Select(scenario => new BattleSession(BattleDemoContent.CreateChallenge(scenario), 52))
        .ToArray();

    while (sessions[0].Snapshot.Phase == BattlePhase.Planning)
    {
        var baseline = sessions[0].Snapshot;
        foreach (var session in sessions)
        {
            AssertSameRulesState(baseline, session.Snapshot);

            var cards = session.Snapshot.Hand
                .OrderBy(card => card.Definition.Effects[0].Kind == CardEffectKind.GainBenefitPerEarlierCard)
                .Take(BattleSnapshot.QueueCapacity)
                .ToArray();
            for (int slot = 0; slot < cards.Length; slot++)
            {
                Submit(session, new PlaceCard(cards[slot].InstanceId, slot));
            }
            Submit(session, new EndTurn());
        }
    }

    foreach (var session in sessions)
    {
        Equal(BattlePhase.Won, session.Snapshot.Phase);
        AssertSameRulesState(sessions[0].Snapshot, session.Snapshot);
    }
}

static void AssertSameRulesState(BattleSnapshot expected, BattleSnapshot actual)
{
    Equal(expected.Revision, actual.Revision);
    Equal(expected.Phase, actual.Phase);
    Equal(expected.Turn, actual.Turn);
    Equal(expected.Benefit, actual.Benefit);
    Equal(expected.DrawCount, actual.DrawCount);
    Equal(expected.DiscardCount, actual.DiscardCount);
    Equal(HandIds(expected), HandIds(actual));
    Equal(ResolutionOrder(expected), ResolutionOrder(actual));
}

static string HandIds(BattleSnapshot snapshot) =>
    string.Join(",", snapshot.Hand.Select(card => $"{card.InstanceId}:{card.Definition.Id}"));

static string ResolutionOrder(BattleSnapshot snapshot) =>
    string.Join(",", snapshot.LastResolutions.Select(entry =>
        $"{entry.SlotIndex}:{entry.Card.InstanceId}:{entry.BenefitGained}:{entry.CardsDrawn}"));

static CardDefinition Card(string id, CardEffectKind kind, int amount) =>
    new CardDefinition(id, id, new[] { new CardEffect(kind, amount) });

static IBattleSession Start(IEnumerable<CardDefinition> deck, int turns, int target, int draw) =>
    new BattleSession(new ChallengeDefinition(BattleDemoContent.Scenarios[0], turns, target, draw, deck), 52);

static void Place(IBattleSession session, string cardId, int slotIndex)
{
    // 测试按定义 ID 挑一张牌，实际命令用实例 ID 区分手里的多张同名牌。
    var card = session.Snapshot.Hand.First(item => item.Definition.Id == cardId);
    Submit(session, new PlaceCard(card.InstanceId, slotIndex));
}

static void Submit(IBattleSession session, BattleCommand command)
{
    // 模拟界面根据当前快照发命令，每次接受只前进一个版本。
    int previousRevision = session.Snapshot.Revision;
    var result = session.Execute(command, previousRevision);
    Equal(true, result.Accepted);
    Equal(previousRevision + 1, result.Snapshot.Revision);
}

static void Reject(IBattleSession session, BattleCommand command, BattleError error)
{
    var previous = session.Snapshot;
    var result = session.Execute(command, previous.Revision);
    Equal(error, result.Error);
    Equal(false, result.Accepted);
    Equal(previous, result.Snapshot);
    Equal(previous, session.Snapshot);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
