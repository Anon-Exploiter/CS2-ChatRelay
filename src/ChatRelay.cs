using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Events;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json.Serialization;

namespace ChatRelay;

public class ChatRelayConfig : BasePluginConfig
{
    [JsonPropertyName("IgnoreCommands")] public bool IgnoreCommands { get; set; } = true;

    [JsonPropertyName("OnlyShowCommands")] public bool OnlyShowCommands { get; set; } = false;

    [JsonPropertyName("DiscordWebhook")] public string DiscordWebhook { get; set; } = "";

    [JsonPropertyName("EmbedFooter")] public string EmbedFooter { get; set; } = "ChatRelay (1.1) by verneri";

}
public class ChatRelay : BasePlugin, IPluginConfig<ChatRelayConfig>
{
    public override string ModuleName => "ChatRelay";
    public override string ModuleDescription => "send your cs2 server chat messages to discord";
    public override string ModuleAuthor => "verneri";
    public override string ModuleVersion => "1.2";

    public ChatRelayConfig Config { get; set; } = new();

    public void OnConfigParsed(ChatRelayConfig config)
    {
        Config = config;
    }
    public override void Load(bool hotReload)
    {
        Logger.LogInformation($"loaded successfully! (Version {ModuleVersion})");
        RegisterEventHandler<EventPlayerChat>(OnEventPlayerChat);
        RegisterEventHandler<EventPlayerConnectFull>((e, _) => Notify(e.Userid, "joined the server"));
        RegisterEventHandler<EventPlayerDisconnect>((e, _) => Notify(e.Userid, "left the server"));
    }

    HookResult Notify(CCSPlayerController? p, string what)
    {
        if (p != null && p.IsValid && !p.IsBot && !p.IsHLTV)
            _ = SendWebhookMessageAsEmbed(p.PlayerName, $"*{what}*", "", p.SteamID);
        return HookResult.Continue;
    }

    public HookResult OnEventPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var eventplayer = @event.Userid;
        var player = Utilities.GetPlayerFromUserid(eventplayer);
        if (player == null || !player.IsValid ||  @event.Text == null)
            return HookResult.Continue;

        if (Config.IgnoreCommands)
        {
            if (@event.Text.Contains('!') || @event.Text.Contains('/'))
                return HookResult.Continue;
        }

        if (Config.OnlyShowCommands)
        {
            if (!@event.Text.Contains('!') && !@event.Text.Contains('/'))
                return HookResult.Continue;
        }

        string playerteam = "[ALL]";
        if (@event.Teamonly)
        {
            switch ((CsTeam)player.TeamNum)
            {
                case CsTeam.Terrorist:
                    playerteam = "[T]";
                    break;
                case CsTeam.CounterTerrorist:
                    playerteam = "[CT]";
                    break;
                case CsTeam.Spectator:
                    playerteam = "[SPEC]";
                    break;
                case CsTeam.None:
                    playerteam = "[NONE]";
                    break;
            }
        }


        _ = SendWebhookMessageAsEmbed(player.PlayerName, @event.Text, playerteam, player.SteamID);
        return HookResult.Continue;
    }

    static readonly HttpClient Http = new();
    static readonly Dictionary<ulong, string> Avatars = new();

    async Task<string> GetAvatar(ulong id)
    {
        if (Avatars.TryGetValue(id, out var a)) return a;
        a = "";
        try
        {
            var xml = await Http.GetStringAsync($"https://steamcommunity.com/profiles/{id}?xml=1");
            var m = System.Text.RegularExpressions.Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]>");
            if (m.Success) a = m.Groups[1].Value;
        }
        catch { }
        Avatars[id] = a;
        return a;
    }

    public async Task SendWebhookMessageAsEmbed(string playerName, string msg, string playerteam, ulong steamID)
    {
        var y = steamID - 76561197960265728UL;
        var payload = new
        {
            username = $"{playerName} [STEAM_1:{y & 1}:{y >> 1}]",
            avatar_url = await GetAvatar(steamID),
            content = $"{playerteam} {msg}",
            allowed_mentions = new { parse = Array.Empty<string>() }
        };
        var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var response = await Http.PostAsync(Config.DiscordWebhook, content);
        if (!response.IsSuccessStatusCode)
            Logger.LogInformation($"Error while sending message to Discord! code: {response.StatusCode}");
    }

}
