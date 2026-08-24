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

        var name = mt.TargetName;
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
            var name = mt.TargetName;
            var player = Svc.Objects.FirstOrDefault(x => x is IPlayerCharacter pc && pc.Name.TextValue == name) as IPlayerCharacter;
            if (player == null)
            {
                Notify.Error("选中失败，请换个位置重试。");
                return;
            }
            SetConductor(player);
        }
    }

    private void ClearConductorClicked(IMenuItemClickedArgs args)
    {
        Conductor.Clear();
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
