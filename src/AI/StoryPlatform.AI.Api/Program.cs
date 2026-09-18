using Microsoft.OpenApi.Models;
using StoryPlatform.AI.Api.Middleware;
using StoryPlatform.AI.Application;
using StoryPlatform.AI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "StoryPlatform AI API",
        Version = "v1",
        Description = "Internal API for guided story generation, refinement and evaluation."
    });
    options.AddSecurityDefinition("InternalApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        Name = "X-Internal-Api-Key",
        In = ParameterLocation.Header,
        Description = "Internal credential shared between Core and AI Service."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "InternalApiKey"
            }
        }] = Array.Empty<string>()
    });
});
builder.Services.AddAIApplication();
builder.Services.AddAIInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "StoryPlatform AI API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseMiddleware<AIExceptionHandlingMiddleware>();
app.UseMiddleware<InternalApiKeyMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
