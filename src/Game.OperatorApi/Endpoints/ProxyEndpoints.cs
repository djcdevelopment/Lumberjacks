using Game.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Game.OperatorApi.Endpoints;

public static class ProxyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/regions", async (IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var url = config["ServiceUrls:Simulation"] ?? "http://localhost:4001";
            var client = httpFactory.CreateClient();
            var response = await client.GetAsync($"{url}/regions");
            var content = await response.Content.ReadAsStringAsync();
            return Results.Content(content, "application/json");
        });

        app.MapGet("/api/players", async (IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var url = config["ServiceUrls:Simulation"] ?? "http://localhost:4001";
            var client = httpFactory.CreateClient();
            var response = await client.GetAsync($"{url}/players");
            var content = await response.Content.ReadAsStringAsync();
            return Results.Content(content, "application/json");
        });

        app.MapGet("/api/events", async (HttpContext context, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var url = config["ServiceUrls:EventLog"] ?? "http://localhost:4002";
            var client = httpFactory.CreateClient();
            var queryString = context.Request.QueryString;
            var response = await client.GetAsync($"{url}/events{queryString}");
            var content = await response.Content.ReadAsStringAsync();
            return Results.Content(content, "application/json");
        });

        app.MapGet("/api/structures", async (HttpContext context, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var url = config["ServiceUrls:Simulation"] ?? "http://localhost:4001";
            var client = httpFactory.CreateClient();
            var queryString = context.Request.QueryString;
            var response = await client.GetAsync($"{url}/structures{queryString}");
            var content = await response.Content.ReadAsStringAsync();
            return Results.Content(content, "application/json");
        });

        app.MapGet("/api/players/{id}/progress", async (string id, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            var url = config["ServiceUrls:Progression"] ?? "http://localhost:4003";
            var client = httpFactory.CreateClient();
            var response = await client.GetAsync($"{url}/players/{id}/progress");
            var content = await response.Content.ReadAsStringAsync();
            return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
        });

        app.MapGet("/api/guilds", async (GameDbContext db) =>
        {
            var guilds = await db.GuildProgress
                .OrderByDescending(g => g.Points)
                .Select(g => new
                {
                    guild_id = g.GuildId,
                    points = g.Points,
                    challenges_completed = g.ChallengesCompleted,
                    updated_at = g.UpdatedAt,
                })
                .ToListAsync();

            return Results.Ok(guilds);
        });
    }
}
