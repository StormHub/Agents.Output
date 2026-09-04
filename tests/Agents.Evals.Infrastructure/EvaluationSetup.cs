using Agents.Api.Options;
using Agents.Api.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Class fixture composing the live system under evaluation out of <c>Agents.Api</c>'s own DI
/// registrations, and owning it for the lifetime of the test class that asks for it.
/// </summary>
/// <remarks>
/// <para>
/// Both suites need a real chat client pointed at the deployment under test, and both would
/// otherwise restate the same three steps: project <see cref="EvaluationOptions"/> onto
/// <c>AgentChatOptions</c>, run production's registration, resolve. Doing it here once means the
/// suites measure the client the API builds — same transport, same options — rather than a
/// lookalike assembled per suite that could drift from it or from each other.
/// </para>
/// <para>
/// What each suite pulls out of the container differs, which is why <see cref="Services"/> hands
/// back the container rather than a client: the trajectory suite resolves the
/// <c>ChatClientAgent</c>, the metrics suite resolves the keyed <c>IChatClient</c> underneath it
/// and, where a judge is configured separately, a second keyed client beside it.
/// </para>
/// <para>
/// Take it as a class fixture — <c>IClassFixture&lt;EvaluationSetup&gt;</c> — and let it hand out
/// the container:
/// </para>
/// <code>
/// public sealed class MyEvalTests(EvaluationSetup setup) : IClassFixture&lt;EvaluationSetup&gt;
/// {
///     [Fact]
///     public async Task Measures()
///     {
///         var agent = setup.Services.GetRequiredService&lt;ChatClientAgent&gt;();
///         // ...
///     }
/// }
/// </code>
/// <para>
/// One container, built inside the fixture and disposed with it once the last test in the class
/// has run. That scoping is the point: a container held statically for the life of the test process
/// outlives every suite that used it, keeps its sockets and its <see cref="IHttpClientFactory"/>
/// alive long after the measurements are over, and leaves a failed run's state visible to the next
/// one. Per class, the deployment a suite talks to is set up and torn down with that suite, and
/// nothing a suite builds leaks into another.
/// </para>
/// </remarks>
public sealed class EvaluationSetup : IAsyncDisposable
{
    private ServiceProvider? _container;

    /// <summary>
    /// The container, built on first use and reused for the rest of the test class.
    /// </summary>
    /// <remarks>
    /// Built lazily rather than in the constructor because xUnit creates the fixture before it
    /// knows whether any test in the class will run: composing the system needs an API key, and
    /// the common case — the offline tiers, and every run in CI — has none and skips. A fixture
    /// that built eagerly would turn those skips into errors.
    /// </remarks>
    /// <value>
    /// A container owned by this fixture. Everything resolved from it is disposed when the fixture
    /// is, so a caller should not dispose what it resolves.
    /// </value>
    public IServiceProvider Services => _container ??= Build();

    /// <summary>
    /// Disposes the container, and with it every chat client and agent resolved from it.
    /// </summary>
    /// <remarks>
    /// Asynchronous because the container's own teardown is: the chat clients underneath it are
    /// registered as transients, so it tracks each one it handed out and releases them here —
    /// through <see cref="IAsyncDisposable"/> where a service offers it. xUnit awaits this after
    /// the last test in the class has finished, and there is nothing to do when no test ever asked
    /// for the container.
    /// </remarks>
    public ValueTask DisposeAsync() => _container?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Composes the system under evaluation, and the judge beside it, from production's own
    /// registrations.
    /// </summary>
    /// <remarks>
    /// The production tools are always registered. The trajectory suite measures whether the agent
    /// routes to them, so it needs the real <c>CalendarDay</c> and <c>WeatherForecast</c>; the
    /// metrics suite resolves only the chat client, which reads no <c>AITool</c> registration at
    /// all — it hands its own stubbed tools to the model through <c>ChatOptions</c>, keeping the
    /// readings fixed and knowable. So one container serves both without either seeing the other's
    /// tools.
    /// </remarks>
    private static ServiceProvider Build()
    {
        var options = EvaluationEnvironment.Current;

        // The production registration rejects a blank key, but from inside Agents.Api the message
        // names AgentChatOptions — a setting nobody configures when running a suite. Say which knob
        // to turn instead.
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                $"No API key for {options.BaseUrl}. "
                + "Set EvaluationOptions__ApiKey, either as an environment variable or with "
                + "`dotnet user-secrets set EvaluationOptions:ApiKey \"...\" "
                + "--project tests/Agents.Evals.Infrastructure`.");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTools();

        // A judge pointed at its own deployment needs its own keyed client, which is what a second
        // pass over the registration adds. It goes first so the system under test is the last
        // AgentChatOptions registered: that singleton is the one production's ChatClientAgent
        // resolves, and the agent this fixture hands out has to be the deployment under evaluation,
        // never the one grading it. Skipped when the judge follows the model — the default — where
        // one keyed client already serves both.
        if (!string.Equals(options.JudgeModel, options.Model, StringComparison.Ordinal))
        {
            services.AddWeatherChatAgent(Project(options, options.JudgeModel));
        }

        services.AddWeatherChatAgent(Project(options, options.Model));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Projects <paramref name="options"/> onto the <c>AgentChatOptions</c> section production
    /// binds, pointed at <paramref name="model"/>.
    /// </summary>
    private static IConfiguration Project(EvaluationOptions options, string model) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [Key(nameof(AgentChatOptions.Model))] = model,
                    [Key(nameof(AgentChatOptions.BaseUrl))] = options.BaseUrl,
                    [Key(nameof(AgentChatOptions.ApiKey))] = options.ApiKey,
                })
            .Build();

    private static string Key(string name) => $"{nameof(AgentChatOptions)}:{name}";
}
