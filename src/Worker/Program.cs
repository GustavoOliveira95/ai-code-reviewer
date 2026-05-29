using Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Application.UseCases.ProcessReview.ProcessReviewHandler).Assembly));
builder.Services.AddInfrastructure(builder.Configuration, registerConsumers: true);

var host = builder.Build();
host.Run();
