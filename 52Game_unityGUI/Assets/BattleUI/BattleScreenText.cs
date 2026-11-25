using System.Collections.Generic;
using Game.Contracts;

namespace Game.UnityUI
{
    /// <summary>用当前剧本术语显示效果和命令失败原因。</summary>
    internal static class BattleScreenText
    {
        internal static string Effect(CardEffect effect, string benefitName)
        {
            int amount = effect.Amount;
            string description = effect.Kind switch
            {
                CardEffectKind.GainBenefit when effect.RepeatCount > 1 => $"{benefitName} +{amount} × {effect.RepeatCount}",
                CardEffectKind.GainBenefit => $"{benefitName} +{amount}",
                CardEffectKind.GainBenefitPerEarlierCard => $"每张前序牌，{benefitName} +{amount}",
                CardEffectKind.GainBenefitPerLaterCard => $"每张后续牌，{benefitName} +{amount}",
                CardEffectKind.GainBenefitPerDrawnCard => $"每实际抽一张牌，{benefitName} +{amount}",
                CardEffectKind.Draw => $"抽 {amount} 张牌",
                _ => string.Empty
            };

            string condition = effect.Condition switch
            {
                CardEffectCondition.FirstCard => "首张结算时，",
                CardEffectCondition.LastCard => "最后结算时，",
                CardEffectCondition.FullQueue => "五槽全满时，",
                CardEffectCondition.PreviousEffectDrewCards => "上一效果抽到牌时，",
                _ => string.Empty
            };

            return condition + description;
        }

        internal static string Keywords(CardDefinition card, bool detailed = false)
        {
            var keywords = new List<string>();
            if (card.HasKeyword(CardKeyword.Gift))
            {
                keywords.Add(detailed ? "馈赠牌入队即结算，使用后移出局内和局外牌组" : "馈赠");
            }

            if (card.HasKeyword(CardKeyword.Exhaust))
            {
                keywords.Add(detailed ? "消耗牌结算后移出本次挑战牌组" : "消耗");
            }

            if (card.HasKeyword(CardKeyword.Emergency))
            {
                keywords.Add(detailed ? "应急牌仅可在最后一回合使用" : "应急");
            }

            return string.Join(detailed ? "；" : " · ", keywords);
        }

        internal static string Error(BattleError error)
        {
            return error switch
            {
                BattleError.StaleSnapshot => "回合状态已更新，请重新拖拽。",
                BattleError.InvalidSlot => "请选择一个结算槽。",
                BattleError.SlotOccupied => "该槽已有卡牌。",
                BattleError.CardUnavailable => "该卡牌已移出当前位置。",
                BattleError.ChallengeEnded => "挑战已结束。",
                BattleError.TargetNotMet => "收益尚未达标。",
                BattleError.QueueNotEmpty => "请先结算或清空队列。",
                BattleError.CardRequiresFinalTurn => "应急牌只能在最后一回合使用。",
                _ => "操作未生效。"
            };
        }
    }
}
