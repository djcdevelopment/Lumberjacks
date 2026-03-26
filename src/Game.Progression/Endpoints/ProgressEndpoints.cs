using System.Text.Json;
using Game.Contracts.Events;
using Game.Persistence;
using Game.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Game.Progression.Endpoints;

public static class ProgressEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/players/{id}/progress", async (string id, GameDbContext db) =>
        {
            var player = await db.PlayerProgress.FindAsync(id);
            if (player is null)
                return Results.NotFound(new { error = "Player not found" });

            return Results.Ok(new
            {
                player_id = player.PlayerId,
                rank = player.Rank,
                points = player.Points,
                updated_at = player.UpdatedAt,
            });
        });

        app.MapGet("/guilds/{id}/progress", async (string id, GameDbContext db) =>
        {
            var guild = await db.GuildProgress.FindAsync(id);
            if (guild is null)
                return Results.NotFound(new { error = "Guild not found" });

            return Results.Ok(new
            {
                guild_id = guild.GuildId,
                points = guild.Points,
                challenges_completed = guild.ChallengesCompleted,
                updated_at = guild.UpdatedAt,
            });
        });

        app.MapPost("/process-event", async (ProcessEventRequest request, GameDbContext db, ChallengeEngine challengeEngine) =>
        {
            try
            {
                if (request.EventType == EventType.StructurePlaced)
                {
                    if (!string.IsNullOrEmpty(request.ActorId))
                    {
                        var player = await db.PlayerProgress.FindAsync(request.ActorId);
                        if (player is null)
                        {
                            player = new PlayerProgressEntity { PlayerId = request.ActorId };
                            db.PlayerProgress.Add(player);
                        }
                        player.Points += 1;
                        player.UpdatedAt = DateTimeOffset.UtcNow;
                    }

                    if (!string.IsNullOrEmpty(request.GuildId))
                    {
                        var guild = await db.GuildProgress.FindAsync(request.GuildId);
                        if (guild is null)
                        {
                            guild = new GuildProgressEntity { GuildId = request.GuildId };
                            db.GuildProgress.Add(guild);
                        }
                        guild.Points += 1;
                        guild.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }
                else if (request.EventType == EventType.PlayerRankChanged)
                {
                    if (!string.IsNullOrEmpty(request.ActorId) &&
                        request.Payload.ValueKind == JsonValueKind.Object &&
                        request.Payload.TryGetProperty("new_rank", out var rankEl) &&
                        rankEl.TryGetInt32(out var newRank))
                    {
                        var player = await db.PlayerProgress.FindAsync(request.ActorId);
                        if (player is null)
                        {
                            player = new PlayerProgressEntity { PlayerId = request.ActorId };
                            db.PlayerProgress.Add(player);
                        }
                        player.Rank = newRank;
                        player.UpdatedAt = DateTimeOffset.UtcNow;
                    }
                }

                await db.SaveChangesAsync();

                // Evaluate challenges for this event
                var completions = await challengeEngine.EvaluateAsync(
                    request.EventType, request.GuildId, request.Payload);

                return Results.Ok(new
                {
                    processed = true,
                    challenges_completed = completions.Select(c => new
                    {
                        challenge_id = c.ChallengeId,
                        challenge_name = c.ChallengeName,
                        guild_id = c.GuildId,
                        points_awarded = c.PointsAwarded,
                    }),
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}

public record ProcessEventRequest
{
    public required string EventType { get; init; }
    public string? ActorId { get; init; }
    public string? GuildId { get; init; }
    public JsonElement Payload { get; init; }
}
