using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

/// The Symphony panel keeps its Discord link tickets in discord_links, a table it creates in our database itself.
/// The table is not part of the EF model, so it is queried with plain parameterised commands.

public sealed partial class ServerDbPostgres
{
    private const string LinkedSql =
        "SELECT EXISTS (SELECT 1 FROM discord_links WHERE user_id = @user AND valid = TRUE AND discord_id IS NOT NULL)";

    // A pending ticket is a row the panel has not claimed yet; the panel stops resolving it after four hours.
    private const string PendingTicketSql =
        """
        SELECT one_time_token FROM discord_links
        WHERE user_id = @user AND discord_id IS NULL AND "timestamp" > NOW() - INTERVAL '4 hours'
        ORDER BY "timestamp" DESC LIMIT 1
        """;

    private const string MintTicketSql =
        "INSERT INTO discord_links (user_id, one_time_token) VALUES (@user, @token)";

    // Keyed on the user id: two attempts from one player queue behind each other, and nobody else waits.
    private const string LockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@user, 0))";

    public override async Task<(bool linked, Guid? ticket)> GetOrMintDiscordLinkTicketAsync(NetUserId userId)
    {
        await using var db = await GetDbImpl();
        await db.PgDbContext.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection) db.PgDbContext.Database.GetDbConnection();

        // One attempt per player at a time. A launcher retry racing the first refusal would mint twice, and the
        // panel burns only the token the player was shown. The lock lives with the transaction, so an early
        // return releases it.
        await using var tx = await connection.BeginTransactionAsync();
        await using (var gate = new NpgsqlCommand(LockSql, connection, tx))
        {
            gate.Parameters.AddWithValue("user", userId.UserId.ToString());
            await gate.ExecuteNonQueryAsync();
        }

        // A live link must never be overwritten, so a linked player gets no ticket at all.
        await using (var linked = new NpgsqlCommand(LinkedSql, connection, tx))
        {
            linked.Parameters.AddWithValue("user", userId.UserId);
            if (await linked.ExecuteScalarAsync() is true)
                return (true, null);
        }

        await using (var pending = new NpgsqlCommand(PendingTicketSql, connection, tx))
        {
            pending.Parameters.AddWithValue("user", userId.UserId);
            if (await pending.ExecuteScalarAsync() is Guid existing)
                return (false, existing);
        }

        var ticket = Guid.NewGuid();
        await using (var mint = new NpgsqlCommand(MintTicketSql, connection, tx))
        {
            mint.Parameters.AddWithValue("user", userId.UserId);
            mint.Parameters.AddWithValue("token", ticket);
            await mint.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return (false, ticket);
    }
}
