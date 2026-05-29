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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerConsumers = false)
    {
        // ── Settings ─────────────────────────────────────────────────────────
        services.Configure<GitHubSettings>(configuration.GetSection(GitHubSettings.Section));

        // ── HTTP client timeout (LLM can be slow on CPU) ─────────────────────
        services.ConfigureHttpClientDefaults(o =>
            o.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(10)));

        // ── Semantic Kernel + Ollama ──────────────────────────────────────────
        var ollamaEndpoint = configuration["Ollama:Endpoint"]
            ?? throw new InvalidOperationException("Ollama:Endpoint is not configured.");

        var kernelBuilder = services.AddKernel();
        kernelBuilder.AddOllamaChatCompletion("llama3.2:3b", new Uri(ollamaEndpoint));

        services.AddSingleton<ILlmReviewService, OllamaReviewService>();

        // ── GitHub ────────────────────────────────────────────────────────────
        services.AddSingleton<IGitHubService, GitHubService>();

        // ── MassTransit + RabbitMQ ────────────────────────────────────────────
        var rabbitHost     = configuration["RabbitMQ:Host"]     ?? "localhost";
        var rabbitUser     = configuration["RabbitMQ:User"]     ?? "guest";
        var rabbitPassword = configuration["RabbitMQ:Password"] ?? "guest";

        services.AddMassTransit(x =>
        {
            if (registerConsumers)
                x.AddConsumer<ReviewRequestConsumer>();

            x.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(rabbitHost, "/", h =>
                {
                    h.Username(rabbitUser);
                    h.Password(rabbitPassword);
                });

                if (registerConsumers)
                    cfg.ConfigureEndpoints(ctx);
            });
        });

        // ── Publisher ─────────────────────────────────────────────────────────
        services.AddScoped<IReviewPublisher, RabbitMqPublisher>();

        return services;
    }
}
