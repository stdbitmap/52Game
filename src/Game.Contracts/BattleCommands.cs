using System;

namespace Game.Contracts
{
    /// <summary>
    /// 单场挑战与界面的接线入口。界面从快照读状态，通过命令提交操作。
    /// 同一会话应在同一线程依次调用，接入 Unity 时建议在主线程处理输入和刷新。
    /// </summary>
    public interface IBattleSession
    {
        /// <summary>会话的当前只读状态，可用于首次渲染或重新同步界面。</summary>
        BattleSnapshot Snapshot { get; }

        /// <summary>
        /// 同步执行一条命令。接受时返回版本递增的新快照，拒绝时返回当前快照和错误原因。
        /// </summary>
        /// <param name="command">拖拽、结束回合或提前完成挑战的操作。</param>
        /// <param name="expectedRevision">交互所依据的快照版本。拖拽时记录拖拽开始时的版本。</param>
        BattleResult Execute(BattleCommand command, int expectedRevision);
    }

    /// <summary>界面提交给战斗核心的操作基类。</summary>
    public abstract class BattleCommand
    {
    }

    /// <summary>将手牌拖入一个空槽，或将已入队的卡牌拖到另一个空槽。放置时仅更新队列。</summary>
    public sealed class PlaceCard : BattleCommand
    {
        public PlaceCard(int instanceId, int slotIndex)
        {
            InstanceId = instanceId;
            SlotIndex = slotIndex;
        }

        /// <summary>被拖拽的卡牌实例 ID。</summary>
        public int InstanceId { get; }

        /// <summary>目标槽位索引，从左至右为 0 到 4。</summary>
        public int SlotIndex { get; }
    }

    /// <summary>将已入队的卡牌拖回手牌，腾出原槽位。</summary>
    public sealed class ReturnCard : BattleCommand
    {
        public ReturnCard(int instanceId)
        {
            InstanceId = instanceId;
        }

        public int InstanceId { get; }
    }

    /// <summary>
    /// 一次性结算所有已入队的卡牌，然后开始下一回合或判定挑战结果。
    /// 界面可按结果快照中的 <see cref="BattleSnapshot.LastResolutions"/> 播放逐卡反馈。
    /// </summary>
    public sealed class EndTurn : BattleCommand
    {
    }

    /// <summary>在规划阶段达成收益目标且待结算队列为空时，可以提前结束挑战并获胜。</summary>
    public sealed class FinishChallenge : BattleCommand
    {
    }

    /// <summary>命令的拒绝原因。界面可用它显示提示并按结果快照重新同步。</summary>
    public enum BattleError
    {
        None,
        /// <summary>操作依据的版本已经过期，例如上一次提交已被接受。</summary>
        StaleSnapshot,
        ChallengeEnded,
        InvalidSlot,
        /// <summary>目标槽位已有卡牌。移动命令要求目标槽为空。</summary>
        SlotOccupied,
        CardUnavailable,
        TargetNotMet,
        QueueNotEmpty
    }

    /// <summary>一次同步命令的接受状态、拒绝原因和提交后的会话快照。</summary>
    public sealed class BattleResult
    {
        public BattleResult(BattleError error, BattleSnapshot snapshot)
        {
            Error = error;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public bool Accepted => Error == BattleError.None;
        public BattleError Error { get; }
        public BattleSnapshot Snapshot { get; }
    }
}
