using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TypedPond.Core;

namespace TypedPond.Service;

/// <summary>Request body for POST /api/steps.</summary>
public record StepUpdateRequest(int Steps, string? Date, string? Hmac);

/// <summary>Response body for GET /api/status.</summary>
public record StatusResponse(bool Locked, int TodaySteps, int Goal);

/// <summary>
/// Minimal API endpoint mappings for the local step HTTP API.
/// </summary>
public static class StepApiController
{
    /// <summary>Maps the step API endpoints onto the application.</summary>
    public static IEndpointRouteBuilder MapStepApi(this IEndpointRouteBuilder app)
    {
        // POST /api/steps — HMAC-authenticated step update pushed by the Android app.
        app.MapPost("/api/steps", async (
            StepUpdateRequest request,
            Config config,
            StepStore stepStore,
            LockManager lockManager,
            ILoggerFactory loggerFactory) =>
        {
            ILogger logger = loggerFactory.CreateLogger("TypedPond.StepApi");

            string date = string.IsNullOrWhiteSpace(request.Date)
                ? DateTime.Now.ToString("yyyy-MM-dd")
                : request.Date;

            bool valid = HmacValidator.ValidateHmac(
                request.Steps.ToString(),
                date,
                request.Hmac ?? string.Empty,
                config.HmacSecret);

            if (!valid)
            {
                logger.LogWarning("Rejected step update for {Date}: invalid HMAC.", date);
                return Results.Unauthorized();
            }

            await stepStore.UpsertStepsAsync(date, request.Steps);
            logger.LogInformation("Accepted step update: {Steps} steps for {Date}.", request.Steps, date);

            // Evaluate the lock off the request thread: Lock() can block for the
            // pipe connect timeout while (re)launching the lock screen, and the
            // client only needs confirmation that the update was accepted.
            _ = Task.Run(() =>
            {
                try
                {
                    lockManager.EvaluateAndUpdate();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Lock evaluation after step update failed.");
                }
            });

            return Results.Ok();
        });

        // GET /api/status — admin/debug view of the current lock state.
        app.MapGet("/api/status", async (
            Config config,
            StepStore stepStore,
            LockManager lockManager) =>
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int? todaySteps = await stepStore.GetStepsAsync(today);

            return Results.Ok(new StatusResponse(
                Locked: lockManager.IsLocked,
                TodaySteps: todaySteps ?? 0,
                Goal: config.StepGoal));
        });

        return app;
    }
}
