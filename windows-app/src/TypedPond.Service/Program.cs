using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.WindowsServices;
using TypedPond.Core;
using TypedPond.Service;

// When running as a Windows Service the working directory is System32, so the
// content root must be pinned to the executable's directory (per MS docs for
// hosting ASP.NET Core in a Windows Service).
var options = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
};

var builder = WebApplication.CreateBuilder(options);

builder.Host.UseWindowsService(serviceOptions =>
{
    serviceOptions.ServiceName = "TypedPond";
});

// Configuration: bind the "TypedPond" section of appsettings.json onto the Core Config.
Config config = builder.Configuration.GetSection("TypedPond").Get<Config>() ?? new Config();
if (string.IsNullOrWhiteSpace(config.DataDirectory))
{
    config.DataDirectory = AppContext.BaseDirectory;
}

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<StepStore>(_ =>
{
    Directory.CreateDirectory(config.DataDirectory);
    return new StepStore(Path.Combine(config.DataDirectory, "steps.db"));
});
builder.Services.AddSingleton<FirebaseClient>(sp => new FirebaseClient(sp.GetRequiredService<Config>()));
builder.Services.AddSingleton<LockManager>();

// Background services.
builder.Services.AddHostedService<FirebasePollWorker>();
builder.Services.AddHostedService<MidnightResetWorker>();
builder.Services.AddHostedService<MdnsAdvertiser>();

// Kestrel: listen on the configured local port (default 8787) on all interfaces
// so the Android companion app can reach us over the LAN.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(config.LocalHttpPort);
});

var app = builder.Build();

// Prepare the step store before serving traffic.
await app.Services.GetRequiredService<StepStore>().InitializeAsync();

// The machine always starts the day locked; unlock only happens once the goal is met.
app.Services.GetRequiredService<LockManager>().Lock();

// HTTP API (minimal API endpoints).
app.MapStepApi();

app.Run();
