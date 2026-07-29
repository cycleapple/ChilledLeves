using ChilledLeves.Utilities;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChilledLeves.Ui
{
    internal class AlertSettings : Window
    {
        public AlertSettings() : base("Chilled Leves Alert Settings ###ChilledLevesAlertSettings")
        {
            Flags = ImGuiWindowFlags.None;

            SizeConstraints = new()
            {
                MinimumSize = new Vector2(300, 300),
                MaximumSize = new Vector2(2000, 2000),
            };
            P.windowSystem.AddWindow(this);
        }

        public void Dispose()
        {
            P.windowSystem.RemoveWindow(this);
        }

        bool blacklist = C.blacklistFeature;
        bool whitelist = C.whitelistFeature;

        public override void Draw()
        {
            if (blacklist && whitelist)
            {
                whitelist = true;
                blacklist = false;

                C.whitelistFeature = whitelist;
                C.blacklistFeature = blacklist;
                C.Save();
            }

            if (ImGui.RadioButton("黑名單", blacklist))
            {
                whitelist = false;
                C.whitelistFeature = whitelist;

                blacklist = true;
                C.blacklistFeature = blacklist;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("除了黑名單中的角色以外，所有角色都會收到通知");
                ImGui.EndTooltip();
            }

            if (ImGui.RadioButton("白名單", whitelist))
            {
                whitelist = true;
                C.whitelistFeature = whitelist;

                blacklist = false;
                C.blacklistFeature = blacklist;
                C.Save();
            }
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("只有白名單中的角色會收到通知");
                ImGui.EndTooltip();
            }

            NotificationAlerts();

            string list = "";
            if (blacklist)
                list = "blacklist";
            else if (whitelist)
                list = "whitelist";

            if (ImGui.Button($"Add character to {list}"))
            {
                var character = Svc.ClientState.LocalPlayer.Name.ToString();
                var id = Svc.ClientState.LocalContentId;

                if (whitelist && !C.whitelistCharacters.ContainsKey(id))
                    C.whitelistCharacters.Add(id, character);

                if (blacklist && !C.blacklistCharacters.ContainsKey(id))
                    C.blacklistCharacters.Add(id, character);

                C.Save();
            }

            if (ImGui.BeginChild("###ChildAlertChild", ImGui.GetContentRegionAvail(), true))
            {
                if (whitelist)
                {
                    foreach (var character in C.whitelistCharacters)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{character.Value}");
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesome.Trash + $"###WL_{character.Value}"))
                        {
                            C.whitelistCharacters.Remove(character);
                            C.Save();
                        }
                        ImGui.PopFont();
                    }
                }

                if (blacklist)
                {
                    foreach (var character in C.blacklistCharacters)
                    {
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text($"{character.Value}");
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (ImGui.Button(FontAwesome.Trash + $"###BL_{character.Value}"))
                        {
                            C.blacklistCharacters.Remove(character);
                            C.Save();
                        }
                        ImGui.PopFont();
                    }
                }

                ImGui.EndChild();
            }
        }

        private Sounds selectedSound = C.Sounds;
        private bool PlaySound = C.PlaySound;
        private bool ShowWindow = C.ShowOverlayAlert;
        private readonly Sounds[] soundValues = Enum.GetValues(typeof(Sounds)).Cast<Sounds>().ToArray();
        private readonly string[] soundNames = Enum.GetValues(typeof(Sounds)).Cast<Sounds>().Select(s => s.ToName()).ToArray();

        private bool SendChat = C.SendChat;
        private int LeveNotificationAmount = C.LeveAlertAmount;

        private void NotificationAlerts()
        {
            int currentIndex = Array.IndexOf(soundValues, selectedSound);
            if (ImGui.Checkbox("###ShowSoundEffect", ref PlaySound))
            {
                if (C.PlaySound != PlaySound)
                {
                    C.PlaySound = PlaySound;
                    C.Save();
                }
            }
            ImGui.AlignTextToFramePadding();
            ImGui.SameLine();
            ImGui.Text("音效");
            if (PlaySound)
            {
                ImGui.SameLine();
                if (ImGui.Combo("###Select Sound_LeveSE", ref currentIndex, soundNames, soundNames.Length))
                {
                    selectedSound = soundValues[currentIndex]; // Update your selected sound
                    C.Sounds = selectedSound; // Set the variable in C
                    UIGlobals.PlaySoundEffect((uint)selectedSound);
                    C.Save();
                }
            }

            ImGui.Spacing();

            if (ImGui.Checkbox("###ChatMessage", ref SendChat))
            {
                if (C.SendChat != SendChat)
                {
                    C.SendChat = SendChat;
                    C.Save();
                }
            }
            ImGui.SameLine();
            ImGui.Text("聊天訊息通知");

            if (ImGui.Checkbox("###ShowAlertWindow", ref ShowWindow))
            {
                if (C.ShowOverlayAlert != ShowWindow)
                {
                    C.ShowOverlayAlert = ShowWindow;
                    C.Save();
                }
            }
            ImGui.SameLine();
            ImGui.Text("顯示通知視窗");

            ImGui.Spacing();

            ImGui.Text("理符額度通知門檻");
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("設定觸發通知的理符額度。");
                ImGui.Text("目前理符額度達到或超過此數值時會通知。");
                ImGui.EndTooltip();
            }
            if (ImGui.SliderInt("###LeveAmount", ref LeveNotificationAmount, 1, 100))
            {
                if (C.LeveAlertAmount != LeveNotificationAmount)
                {
                    C.LeveAlertAmount = LeveNotificationAmount;
                    C.Save();
                }
            }
        }
    }
}
