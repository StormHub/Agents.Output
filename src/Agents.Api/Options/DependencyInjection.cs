using System.ClientModel.Primitives;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

namespace Agents.Api.Options;

internal static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddAgent(IConfiguration configuration) =>
            services
                .AddAGUIServer()
                .AddWeatherChatAgent(configuration);

        /// <summary>
        /// Registers the chat client and agent without the AG-UI hosting layer, so the agent can be
        /// composed outside a web host (evaluation runs, tests).
        /// </summary>
        public IServiceCollection AddWeatherChatAgent(IConfiguration configuration) =>
            services
                .AddChatClient(configuration)
                .AddChatClientAgent();

        private IServiceCollection AddChatClient(IConfiguration configuration)
        {
            var options = configuration.GetSection(nameof(AgentChatOptions)).Get<AgentChatOptions>()
                          ?? throw new InvalidOperationException($"{nameof(AgentChatOptions)} configuration required.");

            var credential = !string.IsNullOrWhiteSpace(options.ApiKey) 
                ? new AzureKeyCredential(options.ApiKey)
                : throw new InvalidOperationException($"{nameof(AgentChatOptions)} api key configuration required.");
        
            services.AddSingleton(options);
            services.AddHttpClient(options.Model);
        
            services.AddKeyedTransient<IChatClient>(options.Model,
                (provider, _) =>
                {
                    var factory = provider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(options.Model);

                    var transport = new HttpClientPipelineTransport(httpClient);
                    var client = new AzureOpenAIClient(
                        new Uri(options.BaseUrl), 
                        credential, 
                        new AzureOpenAIClientOptions
                        {
                            Transport = transport
                        });
                    var chatClient = client.GetChatClient(options.Model);
                    return chatClient.AsIChatClient();
                });

            return services;
        }

        private IServiceCollection AddChatClientAgent()
        {
            services.AddTransient<ChatClientAgent>(provider =>
            {
                var options = provider.GetRequiredService<AgentChatOptions>();
                var chatClient = provider.GetRequiredKeyedService<IChatClient>(options.Model);
                List<AITool> tools = [.. provider.GetServices<AITool>()];

                var chatOptions = new ChatOptions
                {
                    Instructions = WeatherAgent.Instructions,
                    Tools = tools,
                };

                return chatClient.AsAIAgent(
                    new ChatClientAgentOptions
                    {
                        Name = WeatherAgent.Name,
                        ChatOptions = chatOptions,
                    },
                    provider.GetRequiredService<ILoggerFactory>());
            });

            return services;
        }
    }

    public static IEndpointRouteBuilder ConfigureAgent(this IEndpointRouteBuilder builder)
    {
        var agent = builder.ServiceProvider.GetRequiredService<ChatClientAgent>();
        builder.MapAGUIServer("/chat", agent);

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