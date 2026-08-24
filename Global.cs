global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Numerics;
global using System.Text.RegularExpressions;

global using Dalamud.Plugin;
global using Dalamud.Plugin.Services;
global using Dalamud.Game.Text;
global using Dalamud.Game.Text.SeStringHandling;
global using Dalamud.Game.Text.SeStringHandling.Payloads;
global using Dalamud.Game.ClientState.Conditions;
global using Dalamud.Game.ClientState.Objects.SubKinds;
global using Dalamud.Game.ClientState.Objects.Types;
global using Dalamud.Game.Chat;

global using Lumina.Excel.Sheets;

global using ECommons;
global using ECommons.Automation;
global using ECommons.Configuration;
global using ECommons.DalamudServices;
global using ECommons.GameHelpers;
global using ECommons.ImGuiMethods;
global using ECommons.Logging;
global using ECommons.Schedulers;
global using ECommons.Throttlers;

global using static ECommons.GenericHelpers;
global using static AutoHunt.AutoHunt;
global using S = AutoHunt.Services.ServiceManager;
global using Player = ECommons.GameHelpers.LegacyPlayer.Player;
