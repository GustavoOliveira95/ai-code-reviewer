using Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Application.UseCases.HandleWebhook.HandleWebhookHandler).Assembly));
builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: false);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapControllers();
app.Run();
