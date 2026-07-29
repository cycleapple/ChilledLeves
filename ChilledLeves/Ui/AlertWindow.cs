using ChilledLeves.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChilledLeves.Ui
{
    internal class AlertWindow : Window
    {
        public AlertWindow() : base("理符限額提醒｜Chilled Leves ###ChilledLevesNotifPopup", ImGuiWindowFlags.AlwaysAutoResize)
        {
            P.windowSystem.AddWindow(this);
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        public override void Draw()
        {
            ImGui.Text($"此角色的理符受理限額已達提醒門檻！\n" +
                       $"請在達到上限前使用理符受理限額。");
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.Text($"目前理符受理限額：{Utils.Allowances} / 100");
            if (ImGui.Button($"關閉提醒"))
            {
                SoundAlert.refreshOverlay = false;
                P.alertUi.IsOpen = false;
            }
        }
    }
}
