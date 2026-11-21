using System;
using System.Collections.Generic;
using System.Linq;
using Game.Contracts;

namespace Game.Core
{
    /// <summary>
    /// 一场挑战的规则入口。会话持有手牌、队列、抽牌堆和弃牌堆，调用方通过命令修改状态。
    /// </summary>
    public sealed class BattleSession : IBattleSession
    {
        private readonly ChallengeDefinition _definition;
        private readonly Random _random;
        private readonly List<CardInstance> _hand = new List<CardInstance>();
        private readonly List<CardInstance> _drawPile = new List<CardInstance>();
        private readonly List<CardInstance> _discardPile = new List<CardInstance>();
        private readonly CardInstance[] _queue = new CardInstance[BattleSnapshot.QueueCapacity];
        private readonly List<CardResolution> _lastResolutions = new List<CardResolution>();
        private BattlePhase _phase = BattlePhase.Planning;
        private int _turn = 1;
        private int _lastResolvedTurn;
        private long _benefit;

        /// <summary>建立独立会话，洗好起始牌组并发出第一回合手牌。</summary>
        /// <param name="definition">剧本术语、回合上限、收益目标和起始牌组。</param>
        /// <param name="seed">本场洗牌使用的种子，用于重放同一组操作。</param>
        public BattleSession(ChallengeDefinition definition, int seed)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _random = new Random(seed);

            // 同一张卡的定义可以在牌组里出现多次。每一份都拿到独立的实例 ID。
            for (int index = 0; index < definition.StartingDeck.Count; index++)
            {
                _drawPile.Add(new CardInstance(index + 1, definition.StartingDeck[index]));
            }

            ShuffleDrawPile();
            DrawCards(definition.CardsPerTurn);
            Snapshot = CreateSnapshot(0);
        }

        /// <inheritdoc />
        public BattleSnapshot Snapshot { get; private set; }

        /// <inheritdoc />
        public BattleResult Execute(BattleCommand command, int expectedRevision)
        {
            // 界面提交绘制时看到的版本。重复提交或基于旧手牌的拖拽会在修改状态前被拒绝。
            if (expectedRevision != Snapshot.Revision)
            {
                return new BattleResult(BattleError.StaleSnapshot, Snapshot);
            }

            if (_phase != BattlePhase.Planning)
            {
                return new BattleResult(BattleError.ChallengeEnded, Snapshot);
            }

            BattleError error = command switch
            {
                PlaceCard place => Place(place.InstanceId, place.SlotIndex),
                ReturnCard takeBack => ReturnToHand(takeBack.InstanceId),
                EndTurn _ => ResolveTurn(),
                FinishChallenge _ => FinishEarly(),
                _ => throw new ArgumentException("A battle command is required.", nameof(command))
            };

            // 一条成功命令只发布一次快照。结束回合后的快照包含完整的逐卡结算记录。
            if (error == BattleError.None)
            {
                Snapshot = CreateSnapshot(Snapshot.Revision + 1);
            }

            return new BattleResult(error, Snapshot);
        }

        private BattleError Place(int instanceId, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _queue.Length)
            {
                return BattleError.InvalidSlot;
            }

            if (_queue[slotIndex] != null)
            {
                return BattleError.SlotOccupied;
            }

            int handIndex = _hand.FindIndex(card => card.InstanceId == instanceId);
            if (handIndex >= 0)
            {
                _queue[slotIndex] = _hand[handIndex];
                _hand.RemoveAt(handIndex);
                return BattleError.None;
            }

            int sourceSlot = FindQueuedSlot(instanceId);
            if (sourceSlot < 0)
            {
                return BattleError.CardUnavailable;
            }

            _queue[slotIndex] = _queue[sourceSlot];
            _queue[sourceSlot] = null;
            return BattleError.None;
        }

        private BattleError ReturnToHand(int instanceId)
        {
            int sourceSlot = FindQueuedSlot(instanceId);
            if (sourceSlot < 0)
            {
                return BattleError.CardUnavailable;
            }

            _hand.Add(_queue[sourceSlot]);
            _queue[sourceSlot] = null;
            return BattleError.None;
        }

        private int FindQueuedSlot(int instanceId)
        {
            return Array.FindIndex(_queue, card => card?.InstanceId == instanceId);
        }

        private BattleError ResolveTurn()
        {
            _lastResolvedTurn = _turn;
            _lastResolutions.Clear();

            // 先弃掉未入队的手牌。结算期间抽到的新牌保留在手里，供下一回合使用。
            _discardPile.AddRange(_hand);
            _hand.Clear();

            // 槽序决定先后，空槽直接跳过。当前牌完成自身效果后才进入弃牌堆。
            // 因此抽牌能抽到已结算或未入队的牌，但抽不到当前牌或还在排队的牌。
            int remainingCards = _queue.Count(card => card != null);
            int effectDraws = 0;
            for (int slotIndex = 0; slotIndex < _queue.Length; slotIndex++)
            {
                var card = _queue[slotIndex];
                if (card == null)
                {
                    continue;
                }

                remainingCards--;
                var resolution = CardEffectResolver.Resolve(
                    card, slotIndex, _lastResolutions.Count, remainingCards, effectDraws, DrawCards);
                effectDraws += resolution.CardsDrawn;
                _benefit += resolution.BenefitGained;
                _lastResolutions.Add(resolution);
                _queue[slotIndex] = null;
                _discardPile.Add(card);
            }

            // 结算完整条队列后再判断胜负。提前达到收益目标也要结算剩余卡牌。
            if (_turn == _definition.TurnLimit)
            {
                _phase = _benefit >= _definition.BenefitTarget ? BattlePhase.Won : BattlePhase.Lost;
            }
            else
            {
                _turn++;
                DrawCards(_definition.CardsPerTurn);
            }

            return BattleError.None;
        }

        private BattleError FinishEarly()
        {
            if (_benefit < _definition.BenefitTarget)
            {
                return BattleError.TargetNotMet;
            }

            if (Array.Exists(_queue, card => card != null))
            {
                return BattleError.QueueNotEmpty;
            }

            _phase = BattlePhase.Won;
            return BattleError.None;
        }

        private int DrawCards(int count)
        {
            int drawn = 0;
            while (drawn < count)
            {
                // 抽牌堆用完才重洗弃牌堆。两堆都空时保留已抽到的牌并停止。
                if (_drawPile.Count == 0)
                {
                    if (_discardPile.Count == 0)
                    {
                        break;
                    }

                    _drawPile.AddRange(_discardPile);
                    _discardPile.Clear();
                    ShuffleDrawPile();
                }

                int top = _drawPile.Count - 1;
                _hand.Add(_drawPile[top]);
                _drawPile.RemoveAt(top);
                drawn++;
            }

            return drawn;
        }

        private void ShuffleDrawPile()
        {
            // 从列表末尾开始做 Fisher–Yates 洗牌，抽牌同样读取末尾。
            for (int index = _drawPile.Count - 1; index > 0; index--)
            {
                int other = _random.Next(index + 1);
                var card = _drawPile[index];
                _drawPile[index] = _drawPile[other];
                _drawPile[other] = card;
            }
        }

        private BattleSnapshot CreateSnapshot(int revision)
        {
            return new BattleSnapshot(
                revision, _definition.Scenario, _phase, _turn, _definition.TurnLimit,
                _benefit, _definition.BenefitTarget, _hand, _queue, _drawPile.Count,
                _discardPile.Count, _lastResolvedTurn, _lastResolutions);
        }
    }
}
