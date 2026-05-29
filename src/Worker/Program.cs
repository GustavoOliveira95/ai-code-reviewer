// ============================================================
// WORKER — Program.cs (Startup)
//
// The Worker is a separate .NET process (Worker Service template).
// It has no HTTP endpoints — it only listens to the RabbitMQ queue.
//
// KEY CONCEPT — .NET Worker Service:
// A Worker Service is a long-running background process built
// with the Generic Host (IHost). It is ideal for:
//   - Message queue consumers
//   - Scheduled tasks
//   - Background data processing
//
// It shares the same DI, logging, configuration, and hosted services
// infrastructure as ASP.NET Core, but without the HTTP server.
//
// KEY CONCEPT — Separation of Api and Worker:
// Having two separate processes provides:
//   - Independent scaling: run 3 Worker instances to process reviews in parallel
//   - Fault isolation: if the Worker crashes, the Api keeps accepting webhooks
//   - Separate deployment: update the Worker without touching the Api
//   - Different resource profiles: the Worker needs more memory/CPU for the LLM
//
// KEY CONCEPT — Worker startup vs Api startup:
// The only difference from Api/Program.cs is:
//   - Uses Host.CreateApplicationBuilder (no HTTP server)
//   - Scans ProcessReviewHandler's assembly for MediatR handlers
//   - Passes registerConsumers: true to start listening to RabbitMQ
// ============================================================

using Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Register MediatR with ProcessReviewHandler's assembly.
// The Worker only needs the "process review" use case —
// it never handles webhooks directly.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Application.UseCases.ProcessReview.ProcessReviewHandler).Assembly));

// Register infrastructure with registerConsumers: true.
// This tells MassTransit to start the ReviewRequestConsumer
// and begin listening to the RabbitMQ queue on startup.
builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: true);

var host = builder.Build();

// host.Run() starts all IHostedService implementations (including MassTransit's bus)
// and blocks until the process is shut down (Ctrl+C / SIGTERM).
host.Run();
