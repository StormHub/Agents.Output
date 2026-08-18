using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Agents.Api.Options;

internal static class DependencyInjection
{
    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration) =>
        services
            .AddAGUI()
            .AddChatClient(configuration)
            .AddChatClientAgent();

    private static IServiceCollection AddChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(nameof(AgentChatOptions)).Get<AgentChatOptions>()
                      ?? throw new InvalidOperationException($"{nameof(AgentChatOptions)} configuration required.");

        services.AddSingleton(options);
        services.AddHttpClient(options.Model)
            .ConfigureHttpClient(client => { client.BaseAddress = new Uri(options.BaseUrl); });

        services.AddKeyedTransient<IChatClient>(options.Model,
            (provider, _) =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                var httpClient = factory.CreateClient(options.Model);
                var ollamaApiClient = new OllamaApiClient(httpClient, options.Model);
                return ollamaApiClient;
            });

        return services;
    }

    private static IServiceCollection AddChatClientAgent(this IServiceCollection services) =>
        services.AddTransient<ChatClientAgent>(provider =>
        {
            var options = provider.GetRequiredService<AgentChatOptions>();
            var chatClient = provider.GetRequiredKeyedService<IChatClient>(options.Model);
            List<AITool> tools = [..provider.GetServices<AITool>()];

            var chatOptions = new ChatOptions
            {
                Instructions = "You are a helpful assistant that answers questions.",
                Tools = tools,
            };

            return chatClient.AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "WeatherChat",
                    ChatOptions = chatOptions,
                },
                provider.GetRequiredService<ILoggerFactory>());
        });

    public static IEndpointRouteBuilder ConfigureAgent(this IEndpointRouteBuilder builder)
    {
        var agent = builder.ServiceProvider.GetRequiredService<ChatClientAgent>();
        builder.MapAGUI("/chat", agent);

        builder.MapGet("/health",
            async Task<IStatusCodeHttpResult> (HttpContext context, CancellationToken cancellationToken) =>
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("HealthCheck");
                try
                {
                    var options = context.RequestServices.GetRequiredService<AgentChatOptions>();
                    var factory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(options.Model);
                    var response = await httpClient.GetAsync("/api/version", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return TypedResults.Ok();
                    }
                    
                    return TypedResults.Problem(
                        statusCode: (int)response.StatusCode, 
                        detail: "Chat client not healthy.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Health check failed.");
                }
                
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable, 
                    detail: "Chat client unavailable.");

            });

        return builder;
    }
}