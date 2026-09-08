using StoryPlatform.AI.Api.Middleware;
using StoryPlatform.AI.Application;
using StoryPlatform.AI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddAIApplication();
builder.Services.AddAIInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<AIExceptionHandlingMiddleware>();
app.UseMiddleware<InternalApiKeyMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
