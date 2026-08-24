using System.Reflection;
var dev = @"C:/Users/16699/AppData/Roaming/XIVLauncherCN/addon/Hooks/dev";
var libs = @"C:/Users/16699/WorkBuddy/shiro/AutoHunt/libs";
AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
{
    var n = new AssemblyName(e.Name).Name;
    var p = Path.Combine(dev, n + ".dll");
    if (File.Exists(p)) return Assembly.LoadFrom(p);
    p = Path.Combine(libs, n + ".dll");
    return File.Exists(p) ? Assembly.LoadFrom(p) : null;
};
// 检查 IBattleNpc 的属性
var dal = Assembly.LoadFrom(Path.Combine(dev, "Dalamud.dll"));
var t = dal.GetType("Dalamud.Game.ClientState.Objects.Types.IBattleNpc");
foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    Console.WriteLine($"IBattleNpc.{p.Name}: {p.PropertyType.Name}");
// 检查 ICharacter 的 Health
var tc = dal.GetType("Dalamud.Game.ClientState.Objects.Types.ICharacter");
foreach (var p in tc.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    if (p.Name.Contains("Hp") || p.Name.Contains("Health"))
        Console.WriteLine($"ICharacter.{p.Name}: {p.PropertyType.Name}");
