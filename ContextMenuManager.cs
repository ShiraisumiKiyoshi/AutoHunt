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
        if (Conductor.IsConductor(name))
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
        if (args.Target is MenuTargetDefault mt && mt.TargetName != null)
        {
            Conductor.Remove(StripWorldSuffix(mt.TargetName));
        }
    }

    /// <summary>
    /// 按名字添加车头（多车头追加，同名去重）：玩家在附近时记录世界服并焦点；
    /// 不在附近（跨图、太远、对象表无此人）时同样生效——只按名字识别聊天消息，
    /// 等玩家出现后 EnsureFocus 会自动补上焦点。
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

        Conductor.Add(name, 0, focusIfNearby: false);
        HuntController.Reset();
    }

    /// <summary>
    /// 添加车头：记录名称与服务器，选中并焦点该玩家。
    /// </summary>
    public static void SetConductor(IPlayerCharacter player)
    {
        Conductor.Add(player.Name.TextValue, player.HomeWorld.RowId);
        Svc.Targets.Target = player;
        Svc.Targets.FocusTarget = player;
        HuntController.Reset();
    }

    public void Dispose()
    {
        Svc.ContextMenu.OnMenuOpened -= OpenContextMenu;
    }
}
