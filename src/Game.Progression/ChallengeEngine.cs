using System.Text.Json;
using Game.Persistence;
using Game.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Game.Progression;

public class ChallengeEngine
{
    private readonly GameDbContext _db;
    private readonly ILogger<ChallengeEngine> _logger;

    public ChallengeEngine(GameDbContext db, ILogger<ChallengeEngine> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<ChallengeCompletionResult>> EvaluateAsync(
        string eventType, string? guildId, JsonElement payload, int incrementValue = 1)
    {
        var results = new List<ChallengeCompletionResult>();
        if (string.IsNullOrEmpty(guildId)) return results;

        var now = DateTimeOffset.UtcNow;

        // Find active challenges that trigger on this event type
        var challenges = await _db.Challenges
            .Where(c => c.Active && c.TriggerEvent == eventType)
            .Where(c => c.WindowStart == null || c.WindowStart <= now)
            .Where(c => c.WindowEnd == null || c.WindowEnd >= now)
            .ToListAsync();

        foreach (var challenge in challenges)
        {
            if (!MatchesFilters(challenge.TriggerFilters, payload))
                continue;

            // Get or create progress row
            var progress = await _db.ChallengeProgress
                .FirstOrDefaultAsync(p => p.ChallengeId == challenge.Id && p.GuildId == guildId);

            if (progress is null)
            {
                progress = new ChallengeProgressEntity
                {
                    ChallengeId = challenge.Id,
                    GuildId = guildId,
                };
                _db.ChallengeProgress.Add(progress);
            }

            if (progress.Completed) continue;

            // Increment based on progress mode
            progress.CurrentValue = challenge.ProgressMode switch
            {
                "sum" => progress.CurrentValue + incrementValue,
                "max" => Math.Max(progress.CurrentValue, incrementValue),
                "count" => progress.CurrentValue + 1,
                _ => progress.CurrentValue + incrementValue,
            };
            progress.UpdatedAt = DateTimeOffset.UtcNow;

            // Check completion
            if (progress.CurrentValue >= challenge.Target)
            {
                progress.Completed = true;
                progress.CompletedAt = DateTimeOffset.UtcNow;

                // Award guild points from rewards
                var rewardPoints = ParseRewardPoints(challenge.Rewards);
                if (rewardPoints > 0)
                {
                    var guild = await _db.GuildProgress.FindAsync(guildId);
                    if (guild is null)
                    {
                        guild = new GuildProgressEntity { GuildId = guildId };
                        _db.GuildProgress.Add(guild);
                    }
                    guild.Points += rewardPoints;
                    guild.ChallengesCompleted += 1;
                    guild.UpdatedAt = DateTimeOffset.UtcNow;
                }

                results.Add(new ChallengeCompletionResult(
                    challenge.Id, challenge.Name, guildId, rewardPoints));

                _logger.LogInformation(
                    "Challenge '{Name}' completed by guild {GuildId} — awarded {Points} points",
                    challenge.Name, guildId, rewardPoints);
            }
        }

        await _db.SaveChangesAsync();
        return results;
    }

    private static bool MatchesFilters(string filtersJson, JsonElement payload)
    {
        if (filtersJson is "{}" or "") return true;

        try
        {
            using var doc = JsonDocument.Parse(filtersJson);
            foreach (var filter in doc.RootElement.EnumerateObject())
            {
                if (!payload.TryGetProperty(filter.Name, out var val))
                    return false;

                if (val.ToString() != filter.Value.ToString())
                    return false;
            }
            return true;
        }
        catch
        {
            return true; // If filters are malformed, don't block progress
        }
    }

    private static int ParseRewardPoints(string rewardsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rewardsJson);
            foreach (var reward in doc.RootElement.EnumerateArray())
            {
                if (reward.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "guild_points" &&
                    reward.TryGetProperty("amount", out var amountEl))
                {
                    return amountEl.GetInt32();
                }
            }
        }
        catch { }
        return 0;
    }
}

public record ChallengeCompletionResult(
    string ChallengeId, string ChallengeName, string GuildId, int PointsAwarded);
