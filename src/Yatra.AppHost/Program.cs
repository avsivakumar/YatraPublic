using Yatra.Engine.Api;
using Yatra.Engine.DependencyInjection;
using Yatra.Orchestration.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYatraEngine(builder.Configuration);
builder.Services.AddSampleYatraOrchestration();
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Yatra:AllowedCorsOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];

    options.AddPolicy(
        "YatraUi",
        policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors("YatraUi");
app.MapYatraEngineApi();

app.Run();

public partial class Program;
