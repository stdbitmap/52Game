using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UnityUI
{
    /// <summary>为路线和战斗界面统一设置中文排印与独立的 HUD 数字。</summary>
    internal sealed class UiTypography
    {
        // 使用随项目提供的静态字体，避免其他设备未安装字体或 Unity 无法读取系统可变字体。
        private readonly Font _body = Resources.Load<Font>("SourceHanSansCN-Regular");
        private readonly Font _hudNumber = Resources.Load<Font>("ChakraPetch-Bold");

        public void ApplyBody(VisualElement root)
        {
            if (_body)
            {
                // UI Toolkit 子元素继承根元素上的正文家族，标题的大小和字重由 USS 处理。
                root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_body));
            }
        }

        // 只用于独立的大号读数和关键层编号。说明、计数和列表继续继承正文的字体。
        public void ApplyHudNumber(Label label)
        {
            if (_hudNumber)
            {
                label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(_hudNumber));
                // 文件本身就是粗体，避免 UI Toolkit 再合成一层粗体。
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
            }
        }
    }
}
