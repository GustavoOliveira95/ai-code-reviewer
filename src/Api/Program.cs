// ============================================================
// API — Program.cs (Startup / Composition Root)
//
// In .NET 6+ the old Startup.cs was replaced by top-level
// statements in Program.cs. This is the API's entry point.
//
// KEY CONCEPT — WebApplication Builder pattern:
// Everything follows the "builder → build → configure → run" pattern:
//   1. Create a builder and register services (DI configuration)
//   2. Call builder.Build() to create the WebApplication
//   3. Configure the HTTP pipeline (middleware)
//   4. Call app.Run() to start listening for requests
//
// KEY CONCEPT — Api vs Worker startup:
// The Api registers MediatR with HandleWebhookHandler's assembly
// because it only needs webhook-related use cases.
// It passes registerConsumers: false to AddInfrastructure()
// because the Api does NOT consume messages — only publishes them.
// ============================================================

using Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Register MVC controllers (enables [ApiController] routing)
builder.Services.AddControllers();

// Register OpenAPI/Swagger docs (available at /openapi/v1.json in Development)
builder.Services.AddOpenApi();

// Register MediatR and scan the Application assembly for all IRequestHandler<T> implementations.
// This single line makes HandleWebhookHandler discoverable by the DI container.
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Application.UseCases.HandleWebhook.HandleWebhookHandler).Assembly));

// Register all infrastructure services (GitHub, Ollama, MassTransit publisher).
// registerConsumers: false → the Api only PUBLISHES to RabbitMQ, never consumes.
builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: false);

var app = builder.Build();

// Only expose the OpenAPI docs in Development — never in Production.
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// Enable attribute-based routing defined in controllers ([Route], [HttpPost], etc.)
app.MapControllers();

app.Run();
