using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoHunt.Services;

/// <summary>
/// 使用统计上报：插件加载后按固定间隔上报「角色名 + 所在服务器 + 大区 + 插件版本」，
/// 用于后台统计累计使用人数与当前在线人数（在线 = 最近一次上报在设定时限内）。
/// 上报失败一律静默忽略，不阻塞主循环、不产生任何提示。
/// </summary>
internal static class StatsReporter
{
    private const string HeartbeatUrl = "https://autohunt-stats.app.workbuddy.host/.cloud/database/rest/v1/rpc/ah_heartbeat";
    private const string AccessKey = "wbpk_pMtzKnq7EiL2iVBqxxN1hv_fWz0jh5sIur1kp3PGU75fe0d2AZMO3ZG";

    /// <summary>上报间隔（毫秒）。</summary>
    private const long IntervalMs = 5 * 60 * 1000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static long lastReportTick;
    private static bool sending;
    private static bool lastLogFailed;

    /// <summary>主循环调用：到点后异步上报一次（不等待返回）。</summary>
    public static void Update()
    {
        if (!Player.Available) return;
        var now = Environment.TickCount64;
        if (lastReportTick != 0 && now - lastReportTick < IntervalMs) return;
        lastReportTick = now;
        _ = ReportAsync();
    }

    private static async Task ReportAsync()
    {
        if (sending) return;
        sending = true;
        try
        {
            var name = Player.Name;
            if (string.IsNullOrWhiteSpace(name)) return;
            var world = GetWorldName();
            if (string.IsNullOrWhiteSpace(world)) return;

            var payload = JsonSerializer.Serialize(new
            {
                p_user_key = $"{name}@{world}",
                p_character_name = name,
                p_world = world,
                p_data_center = GetDataCenterName(),
                p_plugin_version = typeof(AutoHunt).Assembly.GetName().Version?.ToString() ?? "",
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, HeartbeatUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("x-wb-webapp-access-key", AccessKey);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode && !lastLogFailed)
            {
                lastLogFailed = true;
                PluginLog.Warning($"[AutoHunt] 统计上报返回 {(int)resp.StatusCode}（不影响功能）");
            }
            else if (resp.IsSuccessStatusCode)
            {
                lastLogFailed = false;
            }
        }
        catch (Exception e)
        {
            if (!lastLogFailed)
            {
                lastLogFailed = true;
                PluginLog.Warning($"[AutoHunt] 统计上报失败（不影响功能）: {e.Message}");
            }
        }
        finally
        {
            sending = false;
        }
    }

    /// <summary>角色当前所在服务器名称。</summary>
    private static string GetWorldName()
    {
        try
        {
            var cw = Player.Object.CurrentWorld;
            if (cw.RowId != 0) return cw.Value.Name.ToString();
            var hw = Player.Object.HomeWorld;
            return hw.RowId != 0 ? hw.Value.Name.ToString() : "";
        }
        catch { return ""; }
    }

    /// <summary>角色所在大区名称。</summary>
    private static string GetDataCenterName()
    {
        try
        {
            var cw = Player.Object.CurrentWorld;
            if (cw.RowId != 0) return cw.Value.DataCenter.Value.Name.ToString();
            var hw = Player.Object.HomeWorld;
            return hw.RowId != 0 ? hw.Value.DataCenter.Value.Name.ToString() : "";
        }
        catch { return ""; }
    }
}
