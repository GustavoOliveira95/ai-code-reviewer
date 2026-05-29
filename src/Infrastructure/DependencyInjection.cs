// ============================================================
// INFRASTRUCTURE LAYER — DependencyInjection (Composition Root)
//
// KEY CONCEPT — Composition Root:
// This is where we "wire everything together". We register all
// concrete implementations against their interfaces so the DI
// container knows what to inject when a class asks for an abstraction.
//
// KEY CONCEPT — Extension Method pattern:
// AddInfrastructure() is an extension method on IServiceCollection.
// This keeps Program.cs clean: one line registers everything.
// Both Api and Worker call this method, which is why 'registerConsumers'
// exists — the Api doesn't need the MassTransit consumer running,
// only the Worker does.
//
// Dependency graph registered here:
//   IGitHubService    → GitHubService
//   ILlmReviewService → OllamaReviewService
//   IReviewPublisher  → RabbitMqPublisher
//   IChatCompletionService → OllamaChatCompletionService (via SK)
//   MassTransit bus   → RabbitMQ transport
// ============================================================

using Application.Interfaces;
using Application.Settings;
using Infrastructure.GitHub;
using Infrastructure.Llm;
using Infrastructure.Messaging;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Infrastructure;

public static class DependencyInjection
{
    // 'registerConsumers' flag: true in Worker (consumes from queue),
    // false in Api (only publishes to queue).
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = false)
    {
        // ── Options Pattern: bind config section to typed settings class ──────
        // This reads "GitHub": { "Token": "...", "WebhookSecret": "..." }
        // from appsettings.json / environment variables and maps it to
        // GitHubSettings. Then IOptions<GitHubSettings> can be injected anywhere.
        services.Configure<GitHubSettings>(configuration.GetSection(GitHubSettings.Section));

        // ── Global HTTP client timeout ────────────────────────────────────────
        // Ollama running on CPU can take several minutes to respond.
        // The default HttpClient timeout is 100 seconds — way too short.
        // ConfigureHttpClientDefaults applies this timeout to ALL HttpClients
        // created by the DI container, including Semantic Kernel's internal ones.
        services.ConfigureHttpClientDefaults(o =>
            o.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(10)));

        // ── Semantic Kernel + Ollama ──────────────────────────────────────────
        // AddKernel() registers Semantic Kernel's core services.
        // AddOllamaChatCompletion() registers IChatCompletionService backed
        // by Ollama's REST API, pointing at our local Ollama container.
        // The model name "llama3.2:3b" must match what ollama-init pulled.
        var ollamaEndpoint = configuration["Ollama:Endpoint"]
            ?? throw new InvalidOperationException("Ollama:Endpoint is not configured.");

        var kernelBuilder = services.AddKernel();
        kernelBuilder.AddOllamaChatCompletion("llama3.2:3b", new Uri(ollamaEndpoint));

        // AddSingleton: one instance for the entire application lifetime.
        // Safe here because OllamaReviewService holds no mutable state.
        services.AddSingleton<ILlmReviewService, OllamaReviewService>();

        // ── GitHub ────────────────────────────────────────────────────────────
        // GitHubService wraps Octokit.NET. Also registered as Singleton
        // because GitHubClient is thread-safe and reuse is recommended.
        services.AddSingleton<IGitHubService, GitHubService>();

        // ── MassTransit + RabbitMQ ────────────────────────────────────────────
        // AddMassTransit() registers the bus infrastructure:
        //   - IBus, IPublishEndpoint, ISendEndpointProvider (for the publisher)
        //   - A hosted service that keeps the connection to RabbitMQ alive
        //   - Auto-retry, error queues, and serialization configuration
        var rabbitHost     = configuration["RabbitMQ:Host"]     ?? "localhost";
        var rabbitUser     = configuration["RabbitMQ:User"]     ?? "guest";
        var rabbitPassword = configuration["RabbitMQ:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            // Only the Worker registers consumers — it's the one that
            // needs to listen and react to messages in the queue.
            // The Api only publishes messages (no consumer registered there).
            if (registerConsumers)
                x.AddConsumer<ReviewRequestConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                // "/" is the default RabbitMQ virtual host.
                // In multi-tenant setups you'd use separate vhosts per environment.
                cfg.Host(rabbitHost, "/", h =>
                {
                    h.Username(rabbitUser);
                    h.Password(rabbitPassword);
                });

                // ConfigureEndpoints scans all registered consumers and
                // automatically creates the RabbitMQ queues and exchanges for them.
                // Queue name is derived from the message type:
                //   ReviewRequestedMessage → "review-requested-message"
                if (registerConsumers)
                    cfg.ConfigureEndpoints(ctx);
            });
        });

        // ── Publisher ─────────────────────────────────────────────────────────
        // AddScoped: one instance per HTTP request (Api) or per message (Worker).
        // IPublishEndpoint (injected into RabbitMqPublisher) is also scoped
        // by MassTransit, so lifetimes must match.
        services.AddScoped<IReviewPublisher, RabbitMqPublisher>();

        return services;
    }
}
