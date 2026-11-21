using System;

namespace Game.Contracts
{
    /// <summary>
    /// 一个剧本的识别 ID 和展示术语。四个剧本使用同一套战斗规则，界面从这里读取当前剧本的名称。
    /// </summary>
    public sealed class ScenarioTerms
    {
        public ScenarioTerms(string id, string name, string benefitName, string facilityName)
        {
            Id = RequireText(id, nameof(id));
            Name = RequireText(name, nameof(name));
            BenefitName = RequireText(benefitName, nameof(benefitName));
            FacilityName = RequireText(facilityName, nameof(facilityName));
        }

        /// <summary>用于选择和查找剧本的稳定 ID。</summary>
        public string Id { get; }

        /// <summary>用于剧本选择界面和挑战标题的名称。</summary>
        public string Name { get; }

        /// <summary>收益计数的显示名，例如科技点。</summary>
        public string BenefitName { get; }

        /// <summary>设施类内容在当前剧本中的叫法，例如保护区或子公司。</summary>
        public string FacilityName { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A scenario term is required.", parameterName);
            }

            return value;
        }
    }
}
