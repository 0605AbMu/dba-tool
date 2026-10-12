using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Models;

namespace DbaTool.Api.Endpoints;

public static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config").WithTags("YAML Configuration");

        group.MapGet("/yaml", GetYamlAsync);
        group.MapPut("/yaml", UpdateYamlAsync);
        group.MapPost("/regenerate", RegenerateYamlAsync);

        return app;
    }

    private static async Task<IResult> GetYamlAsync(
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var yamlContent = await cfg.GetYamlContentAsync(ct);
        return Results.Ok(new
        {
            configPath = cfg.ConfigPath,
            yamlContent
        });
    }

    private static async Task<IResult> UpdateYamlAsync(
        UpdateYamlRequest req,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        try
        {
            var saved = await cfg.SaveYamlContentAsync(req.YamlContent, ct);
            return Results.Ok(new
            {
                configPath = cfg.ConfigPath,
                yamlContent = saved
            });
        }
        catch (Exception ex) when (ex is not DomainValidationException)
        {
            throw new DomainValidationException($"Invalid YAML syntax: {ex.Message}");
        }
    }

    private static async Task<IResult> RegenerateYamlAsync(
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var yamlContent = await cfg.RegenerateYamlAsync(ct);
        return Results.Ok(new
        {
            configPath = cfg.ConfigPath,
            yamlContent
        });
    }
}
