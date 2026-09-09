using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Connection.Whitelist;
using Content.Server.Connection.Whitelist.Conditions;
using Content.Server.Database;
using Content.Server.Symphony;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Symphony;
using Robust.Shared.Network;

namespace Content.Server.Connection;

/// <summary>
/// The Symphony side of a whitelist refusal: the Discord link that gets the player whitelisted, and the re-check
/// on reconnect that makes a revoke hold. Everything lives here; ConnectionManager.cs carries four one-line calls.
/// </summary>
public sealed partial class ConnectionManager
{
    private bool _symphonyWarned;

    /// <summary>
    /// Everyone who has been checked against a running whitelist and passed. The reconnect allowance waves anyone who
    /// was in the round past the whitelist; these players are checked again instead, so an entry removed mid-round
    /// holds. Never expires: a player who once passed a running whitelist has no claim to the mid-round-toggle allowance.
    /// </summary>
    private readonly HashSet<NetUserId> _whitelistPassed = new();

    /// <summary>
    /// Link URLs minted while refusing a connection, keyed by the player, until the deny properties collect them.
    /// </summary>
    private readonly ConcurrentDictionary<NetUserId, string> _symphonyLinks = new();

    /// <summary>
    /// The refusal text for a failed whitelist: the game's own line, and under it the way in, when a manual whitelist
    /// entry is what the player lacks.
    /// </summary>
    private async Task<string> SymphonyWhitelistRefusal(PlayerConnectionWhitelistPrototype whitelist, NetConnectingArgs e, string denyMessage)
    {
        var message = Loc.GetString("whitelist-fail-prefix", ("msg", denyMessage));
        if (!await ManualWhitelistWouldAdmit(whitelist, e.UserData))
            return message;

        return await AppendDiscordLinkTicket(message, e.UserId, e.AuthType);
    }

    /// <summary>
    /// Whether a manual whitelist entry would have let this player in. The conditions are walked in the order
    /// IsWhitelisted walks them, stopping where it would have stopped, and the answer is yes only when the manual
    /// Allow condition was the one that turned them away. A refusal decided earlier, by a blacklist or a note, gets
    /// no link, since linking would not help. Only reached on a refusal, so the second walk costs nothing in play.
    /// </summary>
    private async Task<bool> ManualWhitelistWouldAdmit(PlayerConnectionWhitelistPrototype whitelist, NetUserData data)
    {
        List<IAdminRemarksRecord>? remarks = null;
        List<PlayTime>? playtime = null;

        foreach (var condition in whitelist.Conditions)
        {
            if (condition is ConditionManualWhitelistMembership)
                return condition.Action == ConditionAction.Allow && !await CheckConditionManualWhitelist(data);

            // A condition that decides nothing cannot have stopped the walk, whatever it matched.
            if (condition.Action == ConditionAction.Next)
                continue;

            bool matched;
            switch (condition)
            {
                case ConditionAlwaysMatch:
                    matched = true;
                    break;
                case ConditionManualBlacklistMembership:
                    matched = await CheckConditionManualBlacklist(data);
                    break;
                case ConditionNotesDateRange notes:
                    matched = CheckConditionNotesDateRange(notes, remarks ??= await _db.GetAllAdminRemarks(data.UserId));
                    break;
                case ConditionPlayerCount count:
                    matched = CheckConditionPlayerCount(count);
                    break;
                case ConditionPlaytime time:
                    matched = CheckConditionPlaytime(time, playtime ??= await _db.GetPlayTimes(data.UserId));
                    break;
                case ConditionNotesPlaytimeRange range:
                    matched = CheckConditionNotesPlaytimeRange(
                        range,
                        remarks ??= await _db.GetAllAdminRemarks(data.UserId),
                        playtime ??= await _db.GetPlayTimes(data.UserId));
                    break;
                default:
                    // A condition this file does not know may have decided; better no link than a wrong one.
                    return false;
            }

            // Allow or Deny with a match is where IsWhitelisted returned, before the manual check.
            if (matched)
                return false;
        }

        return false;
    }

    /// <summary>
    /// Appends the player's one-time link URL, or a note that their Discord is already linked, to a whitelist refusal.
    /// Guests are told to sign in instead. Falls back to the plain message when symphony.url is unset or no ticket
    /// can be had; a refusal never throws.
    /// </summary>
    private async Task<string> AppendDiscordLinkTicket(string message, NetUserId userId, LoginType authType)
    {
        var baseUrl = _cfg.GetCVar(CCVars.SymphonyUrl).Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
            return message;

        // A guest has no account the panel could link, and a guest id is minted per name, or per attempt.
        if (authType != LoginType.LoggedIn)
            return message + "\n" + Loc.GetString("whitelist-discord-guest");

        try
        {
            var (linked, ticket) = await SymphonyLinkTickets.GetOrMintAsync(_cfg, userId);
            if (linked)
                return message + "\n" + Loc.GetString("whitelist-discord-linked");

            if (ticket is { } token)
            {
                // The URL travels as a structured property as well, so the client can offer a button rather than a string to strip out.
                _symphonyLinks[userId] = $"{baseUrl}/auth/start?ticket={token}";
                return message + "\n" + Loc.GetString("whitelist-discord-link");
            }

            WarnSymphonyOnce("only the postgres database engine has a panel bridge");
        }
        catch (Exception e)
        {
            WarnSymphonyOnce($"the ticket could not be stored: {e}");
        }

        return message;
    }

    /// <summary>
    /// Moves the link URL minted for this refusal, if any, into the deny properties the client reads.
    /// </summary>
    private void AddSymphonyLink(NetUserId userId, Dictionary<string, object> properties)
    {
        if (_symphonyLinks.TryRemove(userId, out var url))
            properties[SharedSymphony.LinkKey] = url;
    }

    private void WarnSymphonyOnce(string why)
    {
        if (_symphonyWarned)
            return;

        _symphonyWarned = true;
        _sawmill.Warning($"symphony.url is set but Discord link tickets are unavailable, players get the plain whitelist refusal: {why}");
    }
}
