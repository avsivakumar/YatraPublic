using Yatra.Engine.Api;
using Yatra.Engine.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddYatraEngine(builder.Configuration);

var app = builder.Build();

app.MapYatraEngineApi();

app.Run();

public partial class Program;
