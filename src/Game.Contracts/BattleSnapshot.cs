using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Contracts
{
    /// <summary>当前挑战的阶段，只有规划阶段接受玩家命令。</summary>
    public enum BattlePhase
    {
        Planning,
        Won,
        Lost
    }

    /// <summary>单条卡牌效果的执行结果，供界面按原顺序展示收益和抽牌。</summary>
    public sealed class CardEffectResolution
    {
        public CardEffectResolution(CardEffect effect, long benefitPerGrant, int cardsDrawn)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            BenefitPerGrant = benefitPerGrant;
            CardsDrawn = cardsDrawn;
        }

        public CardEffect Effect { get; }

        /// <summary>套用队列和抽牌计数后，每段实际获得的收益。</summary>
        public long BenefitPerGrant { get; }

        /// <summary>本效果的收益段数。抽牌效果为 0，多段收益可按此数字逐段播放。</summary>
        public int BenefitGrantCount => Effect.Kind == CardEffectKind.Draw ? 0 : Effect.RepeatCount;

        public long BenefitGained => BenefitPerGrant * BenefitGrantCount;
        public int CardsDrawn { get; }
    }

    /// <summary>一张卡牌的结算记录，包含所在槽位、收益与抽牌总数，以及按顺序执行的效果。</summary>
    public sealed class CardResolution
    {
        public CardResolution(CardInstance card, int slotIndex, long benefitGained, int cardsDrawn)
            : this(card, slotIndex, Array.Empty<CardEffectResolution>())
        {
            BenefitGained = benefitGained;
            CardsDrawn = cardsDrawn;
        }

        public CardResolution(CardInstance card, int slotIndex, IEnumerable<CardEffectResolution> effectResolutions)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            SlotIndex = slotIndex;

            var effects = new List<CardEffectResolution>(effectResolutions);
            EffectResolutions = effects.AsReadOnly();
            BenefitGained = effects.Sum(effect => effect.BenefitGained);
            CardsDrawn = effects.Sum(effect => effect.CardsDrawn);
        }

        public CardInstance Card { get; }
        public int SlotIndex { get; }
        public long BenefitGained { get; }
        public int CardsDrawn { get; }

        /// <summary>单卡内按执行顺序排列的只读明细。仅提供汇总时，此列表为空。</summary>
        public IReadOnlyList<CardEffectResolution> EffectResolutions { get; }
    }

    /// <summary>
    /// 一次读取所得的完整战斗状态。手牌、队列和结算记录均为只读副本，
    /// 界面可保留旧快照，用于比较变化和播放反馈。
    /// </summary>
    public sealed class BattleSnapshot
    {
        /// <summary>从左至右参与结算的固定槽位总数。</summary>
        public const int QueueCapacity = 5;

        public BattleSnapshot(
            int revision,
            ScenarioTerms scenario,
            BattlePhase phase,
            int turn,
            int turnLimit,
            long benefit,
            long benefitTarget,
            IEnumerable<CardInstance> hand,
            IEnumerable<CardInstance> queue,
            int drawCount,
            int discardCount,
            int lastResolvedTurn,
            IEnumerable<CardResolution> lastResolutions)
        {
            var queueSlots = new List<CardInstance>(queue);
            if (queueSlots.Count != QueueCapacity)
            {
                throw new ArgumentException($"The queue must contain {QueueCapacity} slots.", nameof(queue));
            }

            Revision = revision;
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Phase = phase;
            Turn = turn;
            TurnLimit = turnLimit;
            Benefit = benefit;
            BenefitTarget = benefitTarget;
            Hand = new List<CardInstance>(hand).AsReadOnly();
            Queue = queueSlots.AsReadOnly();
            DrawCount = drawCount;
            DiscardCount = discardCount;
            LastResolvedTurn = lastResolvedTurn;
            LastResolutions = new List<CardResolution>(lastResolutions).AsReadOnly();
            CanFinishEarly = phase == BattlePhase.Planning
                && benefit >= benefitTarget
                && queueSlots.All(card => card == null);
        }

        /// <summary>当前挑战的快照版本。提交命令时传回读取操作所依据的版本。</summary>
        public int Revision { get; }
        public ScenarioTerms Scenario { get; }
        public BattlePhase Phase { get; }

        /// <summary>当前回合编号，从 1 开始。挑战结束时保留结束所在的回合编号。</summary>
        public int Turn { get; }
        public int TurnLimit { get; }
        public long Benefit { get; }
        public long BenefitTarget { get; }
        public IReadOnlyList<CardInstance> Hand { get; }

        /// <summary>五个槽位从左至右排列，索引从 0 到 4，值为 null 表示空槽。</summary>
        public IReadOnlyList<CardInstance> Queue { get; }
        public int DrawCount { get; }
        public int DiscardCount { get; }

        /// <summary>最近一次结束回合命令结算的回合编号。尚未结算过任何回合时为 0。</summary>
        public int LastResolvedTurn { get; }

        /// <summary>
        /// 最近一次逐卡结算的记录，顺序与实际执行一致。接受结束回合命令时更新，
        /// 拖拽后的界面刷新可继续展示旧记录。
        /// </summary>
        public IReadOnlyList<CardResolution> LastResolutions { get; }

        /// <summary>规划阶段已达成目标且队列为空时为 true，可用于启用提前完成按钮。</summary>
        public bool CanFinishEarly { get; }
    }
}
