using Game.ServiceDefaults;
using Game.Gateway.WebSocket;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<MessageRouter>();

var app = builder.Build();
app.MapServiceDefaults();
app.UseWebSockets();
app.UseMiddleware<GameWebSocketMiddleware>();

app.Run();
