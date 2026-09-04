using System.Text;
using ECommons.Automation.NeoTaskManager;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHunt.Tasks;

/// <summary>
/// 一键创建怪物狩猎招募（队员招募）任务链。
/// 实现参照 HuntTrainAssistant（国服版 TaskCreateHuntPF）：
/// /pfinder 打开招募面板 → 填写留言 → 队员招募 → 普通招募类型 → 讨伐目标类别 11
/// → （可选）青魔占位：勾选平均品级限制并设为 531 → 职业选择弹窗选青魔 → 确认 → 招募。
/// </summary>
public static unsafe class TaskCreateHuntPF
{
    public static void Enqueue()
    {
        if (!Player.Available)
        {
            Notify.Error("现在不能这么做。");
            return;
        }
        if (P.TaskManager.IsBusy)
        {
            Notify.Error("当前有任务正在执行（如副本区切换），请稍后再试。");
            return;
        }
        if (Player.Object.OnlineStatus.RowId == 26)
        {
            Notify.Error("已经在招募队员！");
            return;
        }
        // 67099/67100/67101：三个资料片的狩猎解锁任务，任一完成即可使用招募
        if (!QuestManager.IsQuestComplete(67099) && !QuestManager.IsQuestComplete(67100) && !QuestManager.IsQuestComplete(67101))
        {
            Notify.Error("怪物狩猎还未解锁，无法创建队员招募。");
            return;
        }

        var blu = P.Config.BluPlaceholder;
        var waitStart = DateTime.UtcNow;
        var cfg = new TaskManagerConfiguration(timeLimitMS: 2000);

        // 招募面板已打开则先关闭，再重新打开（确保状态干净）
        P.TaskManager.Enqueue(() =>
        {
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _) && EzThrottler.Throttle("WYPfinderCmd1"))
            {
                Chat.Instance.ExecuteCommand("/pfinder");
            }
        }, cfg);
        P.TaskManager.Enqueue(() => !TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _), cfg);
        P.TaskManager.Enqueue(() => Chat.Instance.ExecuteCommand("/pfinder"), cfg);

        // 主面板就绪 → 写入留言 → 点击「队员招募」
        P.TaskManager.Enqueue(() =>
        {
            if (TryGetAddonMaster<AddonMaster.LookingForGroup>(out var lfg) && IsAddonReady(lfg.Base) && EzThrottler.Throttle("WYPfinderRMOD"))
            {
                SetComment(P.Config.PfinderString);
                return lfg.RecruitMembersOrDetails();
            }
            return false;
        }, cfg);

        // 招募条件窗口：普通招募类型
        P.TaskManager.Enqueue(() =>
        {
            if (TryGetAddonMaster<AddonMaster.LookingForGroupCondition>(out var m) && IsAddonReady(m.Base))
            {
                m.Normal();
                return true;
            }
            return false;
        }, cfg);

        // 选择讨伐目标类别 11（狩猎）
        P.TaskManager.Enqueue(() =>
        {
            if (TryGetAddonMaster<AddonMaster.LookingForGroupCondition>(out var m) && IsAddonReady(m.Base))
            {
                m.SelectDutyCategory(11);
                return true;
            }
            return false;
        }, cfg);

        // ===== 青魔占位：勾选平均品级限制 =====
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupCondition", out var a) && IsAddonReady(a))
            {
                var checkbox = (AtkComponentCheckBox*)((AtkComponentNode*)a->UldManager.NodeList[25])->Component;
                checkbox->SetChecked(true);
                return true;
            }
            return false;
        }, cfg);

        // ===== 青魔占位：平均品级设为 531 =====
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupCondition", out var a) && IsAddonReady(a))
            {
                var input = (AtkComponentNumericInput*)((AtkComponentNode*)a->UldManager.NodeList[24])->Component;
                input->SetValue(531);
                return true;
            }
            return false;
        }, cfg);

        // ===== 青魔占位：确认条件，打开职业选择弹窗 =====
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupCondition", out var a) && IsAddonReady(a) && a->GetComponentButtonById(58) != null)
            {
                Callback.Fire(a, true, 24, 7, 0);
                return true;
            }
            return false;
        }, cfg);

        // 等待职业选择弹窗出现（5 秒未出现则跳过占位，直接招募）
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if ((DateTime.UtcNow - waitStart).TotalMilliseconds > 5000)
            {
                if (P.Config.Debug) PluginLog.Debug("[AutoHunt] 青魔占位：职业选择界面未出现，跳过");
                return true;
            }
            return TryGetAddonByName<AtkUnitBase>("LookingForGroupSelectRole", out var a) && IsAddonReady(a);
        }, cfg);

        // ===== 职业选择弹窗：选择青魔占位并确认 =====
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupSelectRole", out var a))
            {
                Callback.Fire(a, true, 11, 0);
                return true;
            }
            return false;
        }, cfg);
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupSelectRole", out var a))
            {
                Callback.Fire(a, true, 12, 25);
                return true;
            }
            return false;
        }, cfg);
        P.TaskManager.Enqueue(() =>
        {
            if (!blu) return true;
            if (TryGetAddonByName<AtkUnitBase>("LookingForGroupSelectRole", out var a) && IsAddonReady(a))
            {
                Callback.Fire(a, true, 0);
                return true;
            }
            return false;
        }, cfg);

        // 点击「招募」
        P.TaskManager.Enqueue(() =>
            TryGetAddonMaster<AddonMaster.LookingForGroupCondition>(out var m)
            && IsAddonReady(m.Base)
            && EzThrottler.Throttle("WYPfinderRecruit", 1000)
            && m.Recruit(), cfg);

        // 招募成功（获得招募中状态）→ 关闭面板并聊天栏提示
        P.TaskManager.Enqueue(() =>
        {
            if (Player.Object.OnlineStatus.RowId == 26
                && TryGetAddonByName<AtkUnitBase>("LookingForGroup", out _)
                && EzThrottler.Throttle("WYPfinderCmd2"))
            {
                Chat.Instance.ExecuteCommand("/pfinder");
                var msg = new SeStringBuilder()
                    .AddUiForeground(1)
                    .Append("\uE078 已完成创建怪物狩猎招募")
                    .Append($"\n自由留言: {P.Config.PfinderString}")
                    .Append($"\n青魔占位: {(blu ? "是" : "否")}")
                    .AddUiForegroundOff()
                    .Build();
                Svc.Chat.Print(new XivChatEntry { Message = msg, Type = XivChatType.Echo });
                return true;
            }
            return false;
        }, new TaskManagerConfiguration(timeLimitMS: 5000));
    }

    /// <summary>
    /// 把留言写入招募面板的预填留言（AgentLookingForGroup.StoredRecruitmentInfo.CommentString）。
    /// 限制：不超过 2 行、UTF-8 编码（含结尾 \0）不超过 192 字节；超限自动截断。
    /// </summary>
    private static void SetComment(string s)
    {
        s ??= "";
        s = s.Replace("\r", "");
        var lines = s.Split('\n');
        if (lines.Length > 2) s = lines[0] + "\n" + lines[1];
        while (s.Length > 0 && Encoding.UTF8.GetBytes(s + "\0").Length > 192)
        {
            s = s[..^1];
        }
        AgentLookingForGroup.Instance()->StoredRecruitmentInfo.CommentString = s + "\0";
    }
}
