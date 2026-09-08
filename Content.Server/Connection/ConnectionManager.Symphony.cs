using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Content.Shared.Symphony;
using Robust.Shared.Network;

namespace Content.Server.Connection;

/// <summary>
/// Points players the manual whitelist turned away at the Symphony panel, where linking their Discord gets them whitelisted.
/// </summary>
public sealed partial class ConnectionManager
{
    private bool _symphonyWarned;

    /// <summary>
    /// Everyone who has been checked against a running whitelist and passed. The reconnect allowance waves anyone who was
    /// in the round past the whitelist; these players are checked again instead, so an entry removed mid-round holds.
    /// Never expires: a player who once passed a running whitelist has no claim to the mid-round-toggle allowance.
    /// </summary>
    private readonly HashSet<NetUserId> _whitelistPassed = new();

    /// <summary>
    /// Link URLs minted while refusing a connection, keyed by the player, until the deny properties collect them.
    /// </summary>
    private readonly ConcurrentDictionary<NetUserId, string> _symphonyLinks = new();

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
            var (linked, ticket) = await _db.GetOrMintDiscordLinkTicketAsync(userId);
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
