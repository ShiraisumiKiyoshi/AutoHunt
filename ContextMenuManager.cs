using Dalamud.Game.Gui.ContextMenu;

namespace AutoHunt;

/// <summary>
/// 右键菜单：聊天框等处玩家名上提供"设为车头 / 取消车头"。
/// </summary>
public class ContextMenuManager : IDisposable
{
    private static readonly string[] ValidAddons = new string[]
    {
        null,
        "PartyMemberList",
        "FriendList",
        "FreeCompany",
        "LinkShell",
        "CrossWorldLinkshell",
        "_PartyList",
        "ChatLog",
        "LookingForGroup",
        "BlackList",
        "ContentMemberList",
        "SocialList",
        "ContactList",
    };

    private readonly MenuItem menuItemSet;
    private readonly MenuItem menuItemClear;

    public ContextMenuManager()
    {
        menuItemSet = new MenuItem()
        {
            Name = new SeStringBuilder().AddUiForeground("设为车头", 578).Build(),
            Prefix = SeIconChar.BoxedLetterW,
            PrefixColor = 578,
            OnClicked = SetConductorClicked,
        };
        menuItemClear = new MenuItem()
        {
            Name = new SeStringBuilder().AddUiForeground("取消车头", 578).Build(),
            Prefix = SeIconChar.BoxedLetterX,
            PrefixColor = 578,
            OnClicked = ClearConductorClicked,
        };
        Svc.ContextMenu.OnMenuOpened += OpenContextMenu;
    }

    private void OpenContextMenu(IMenuOpenedArgs args)
    {
        if (args.Target is not MenuTargetDefault mt || mt.TargetName == null) return;
        if (!ValidAddons.Contains(args.AddonName)) return;

        var name = StripWorldSuffix(mt.TargetName);
        if (Conductor.IsValid && name == P.Config.ConductorName)
        {
            args.AddMenuItem(menuItemClear);
        }
        else
        {
            args.AddMenuItem(menuItemSet);
        }
    }

    private void SetConductorClicked(IMenuItemClickedArgs args)
    {
        if (args.Target is MenuTargetDefault mt && mt.TargetName != null)
        {
            SetConductorByName(StripWorldSuffix(mt.TargetName));
        }
    }

    /// <summary>去掉名字里可能带的“@世界服”后缀。</summary>
    internal static string StripWorldSuffix(string rawName)
    {
        var name = rawName.Trim();
        var at = name.LastIndexOf('@');
        return at > 0 ? name[..at].Trim() : name;
    }

    private void ClearConductorClicked(IMenuItemClickedArgs args)
    {
        Conductor.Clear();
    }

    /// <summary>
    /// 按名字设置车头：玩家在附近时记录世界服并选中/焦点；不在附近（跨图、太远、对象表无此人）
    /// 时同样生效——只按名字识别聊天消息，等玩家出现后 EnsureFocus 会自动补上焦点。
    /// </summary>
    public static void SetConductorByName(string rawName)
    {
        var name = StripWorldSuffix(rawName);
        if (name.IsNullOrEmpty()) return;

        var player = Svc.Objects.FirstOrDefault(x => x is IPlayerCharacter pc && pc.Name.TextValue == name) as IPlayerCharacter;
        if (player != null)
        {
            SetConductor(player);
            return;
        }

        // 找不到玩家对象：不影响设置。世界服记 0（不校验世界服，按名字匹配消息）。
        P.Config.ConductorName = name;
        P.Config.ConductorWorldId = 0;
        EzConfig.Save();
        HuntController.Reset();
        Notify.Info($"你已选中{name}为车头~（玩家当前不在附近，未选中/焦点；不影响坐标识别，靠近后会自动焦点）");
    }

    /// <summary>
    /// 设置车头：记录名称与服务器，选中并焦点该玩家。
    /// </summary>
    public static void SetConductor(IPlayerCharacter player)
    {
        P.Config.ConductorName = player.Name.TextValue;
        P.Config.ConductorWorldId = player.HomeWorld.RowId;
        EzConfig.Save();
        Svc.Targets.Target = player;
        Svc.Targets.FocusTarget = player;
        HuntController.Reset();
        Notify.Info($"你已选中{P.Config.ConductorName}为车头~");
    }

    public void Dispose()
    {
        Svc.ContextMenu.OnMenuOpened -= OpenContextMenu;
    }
}
