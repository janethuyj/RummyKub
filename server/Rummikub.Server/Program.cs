using Rummikub.Server.Game;
using Rummikub.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
builder.Services.AddSingleton<GameCoordinator>();

// Allow the Vite dev server (and configured production origins) to reach the hub.
// Room-code play uses no cookies or login, so this policy stays simple.
const string DevCors = "dev-cors";
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors(DevCors);

// Serve the built React client (client/dist copied to wwwroot at publish time).
app.UseDefaultFiles();
app.UseStaticFiles();

// GIT_SHA is baked into the image at build time (see Dockerfile); "dev" when
// running from source. Lets you confirm which commit a deployment is serving:
//   wget -qO- http://localhost:8080/health
var version = Environment.GetEnvironmentVariable("GIT_SHA") ?? "dev";
app.MapGet("/health", () => Results.Ok(new { status = "ok", version }));
app.MapHub<GameHub>("/hub/game");

// SPA fallback: any non-API route returns index.html so client-side routing works.
app.MapFallbackToFile("index.html");

app.Run();
