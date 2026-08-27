using System.Collections.Concurrent;
using Agents.Api.Options;
using Agents.Api.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Composes the live system under evaluation out of <c>Agents.Api</c>'s own DI registrations.
/// </summary>
/// <remarks>
/// <para>
/// Both suites need a real chat client pointed at the deployment under test, and both would
/// otherwise restate the same three steps: project <c>EVAL_*</c> onto <c>AgentChatOptions</c>, run
/// production's registration, resolve. Doing it here once means the suites measure the client the
/// API builds — same transport, same options — rather than a lookalike assembled per suite that
/// could drift from it or from each other.
/// </para>
/// <para>
/// What each suite pulls out of the provider differs, which is why this returns the provider rather
/// than a client: <c>Agents.Api.Evals</c> resolves the <c>ChatClientAgent</c>,
/// <c>Agents.Extensions.Evals</c> resolves the keyed <c>IChatClient</c> underneath it.
/// </para>
/// </remarks>
public static class EvalServices
{
    /// <summary>
    /// One provider per (deployment, tool choice), kept for the life of the test process.
    /// </summary>
    /// <remarks>
    /// The provider owns the <see cref="IHttpClientFactory"/> behind the chat client, so it has to
    /// outlive every client resolved from it. Caching it also lets the judge and the system under
    /// test share a connection pool when they point at the same deployment. A failed build is not
    /// cached: <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/>
    /// does not store an entry when the factory throws, so a run that failed on a missing key can
    /// be retried in-process once it is set.
    /// </remarks>
    private static readonly ConcurrentDictionary<(string Model, bool WithProductionTools), ServiceProvider> Providers = new();

    /// <summary>
    /// Builds (or reuses) a provider wired to <paramref name="model"/> at
    /// <see cref="EvalEnvironment.BaseUrl"/>.
    /// </summary>
    /// <param name="model">The deployment to point at — the system under test, or a judge.</param>
    /// <param name="withProductionTools">
    /// <see langword="true"/> registers the real <c>CalendarDay</c> and <c>WeatherForecast</c>
    /// tools, which call Open-Meteo. <see langword="false"/> — the default — leaves the tool
    /// collection empty so the caller can supply <see cref="StubWeatherTools"/> instead and keep the
    /// readings fixed and knowable.
    /// </param>
    public static IServiceProvider ForLiveModel(string model, bool withProductionTools = false)
    {
        // The production registration rejects a blank key, but from inside Agents.Api the message
        // names AgentChatOptions — a setting nobody configures when running a suite. Say which knob
        // to turn instead.
        if (string.IsNullOrWhiteSpace(EvalEnvironment.ApiKey))
        {
            throw new InvalidOperationException(
                $"No API key for {EvalEnvironment.BaseUrl}. Set EVAL_API_KEY, either as an "
                + "environment variable or with `dotnet user-secrets set EVAL_API_KEY \"...\" "
                + "--project tests/Agents.Evals.Infrastructure`.");
        }

        return Providers.GetOrAdd((model, withProductionTools), static key =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AgentChatOptions:Model"] = key.Model,
                        ["AgentChatOptions:BaseUrl"] = EvalEnvironment.BaseUrl,
                        ["AgentChatOptions:ApiKey"] = EvalEnvironment.ApiKey,
                    })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();

            if (key.WithProductionTools)
            {
                services.AddTools();
            }

            services.AddWeatherChatAgent(configuration);

            return services.BuildServiceProvider();
        });
    }
}
