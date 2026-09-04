using Agents.Api.Options;
using Agents.Api.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
/// The container stays inside. What a suite gets is what it measures — the
/// <see cref="ChatClientAgent"/> for the trajectory suite, the <see cref="IChatClient"/> underneath
/// it for the metrics suite — never the provider they came out of. Handing back a provider would
/// let a suite resolve anything, dispose what it does not own, and measure a system this fixture
/// never composed.
/// </para>
/// <para>
/// Take it as a class fixture — <c>IClassFixture&lt;EvaluationSetup&gt;</c> — and ask it for what
/// you measure:
/// </para>
/// <code>
/// public sealed class MyEvalTests(EvaluationSetup setup) : IClassFixture&lt;EvaluationSetup&gt;
/// {
///     [Fact]
///     public async Task Measures()
///     {
///         var agent = setup.ResolveAgent();
///         // ...
///     }
/// }
/// </code>
/// <para>
/// One container per test class, disposed with the fixture once the last test has run. That
/// scoping is the point: a container held statically for the life of the test process outlives
/// every suite that used it, keeps its sockets and its <see cref="IHttpClientFactory"/> alive long
/// after the measurements are over, and leaves a failed run's state visible to the next one. Per
/// class, the deployment a suite talks to is set up and torn down with that suite, and nothing a
/// suite builds leaks into another.
/// </para>
/// </remarks>
public sealed class EvaluationSetup : IAsyncDisposable
{
    private readonly ServiceCollection _services = new();

    private ServiceProvider? _provider;

    /// <summary>
    /// Registers the system under evaluation, and the judge beside it, out of production's own
    /// registrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The production tools are always registered. The trajectory suite measures whether the agent
    /// routes to them, so it needs the real <c>CalendarDay</c> and <c>WeatherForecast</c>; the
    /// metrics suite resolves only the chat client, which reads no <c>AITool</c> registration at
    /// all — it hands its own stubbed tools to the model through <c>ChatOptions</c>, keeping the
    /// readings fixed and knowable. So one set of registrations serves both without either seeing
    /// the other's tools.
    /// </para>
    /// <para>
    /// Everything but the key is settled here, up front. The key is the exception because
    /// production's registration rejects a blank one where it is registered rather than where it is
    /// used, and xUnit builds this fixture before it knows whether any test in the class will run:
    /// the common case — the offline tiers, and every run in CI — has no key and skips. A run
    /// without one therefore registers nothing and says so if a test asks anyway, rather than
    /// failing every test in the class from a constructor.
    /// </para>
    /// </remarks>
    public EvaluationSetup()
    {
        var options = EvaluationEnvironment.Current;

        _services.AddLogging();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return;
        }

        _services.AddTools();

        // A judge pointed at its own deployment needs its own keyed client, which is what a second
        // pass over the registration adds. It goes first so the system under test is the last
        // AgentChatOptions registered: that singleton is the one production's ChatClientAgent
        // resolves, and the agent this fixture hands out has to be the deployment under evaluation,
        // never the one grading it. Skipped when the judge follows the model — the default — where
        // one keyed client already serves both.
        if (!string.Equals(options.JudgeModel, options.Model, StringComparison.Ordinal))
        {
            _services.AddWeatherChatAgent(Project(options, options.JudgeModel));
        }

        _services.AddWeatherChatAgent(Project(options, options.Model));
    }

    /// <summary>
    /// The agent under evaluation: production's <see cref="ChatClientAgent"/>, with production's
    /// tools, pointed at <see cref="EvaluationOptions.Model"/>.
    /// </summary>
    /// <returns>
    /// An agent owned by this fixture and disposed with it, so the caller keeps it for the test and
    /// disposes nothing.
    /// </returns>
    public ChatClientAgent ResolveAgent() => Provider.GetRequiredService<ChatClientAgent>();

    /// <summary>
    /// The chat client under that agent, for a suite measuring the layer beneath it.
    /// </summary>
    /// <param name="model">
    /// The deployment to talk to: <see cref="EvaluationOptions.Model"/> for the system under test,
    /// <see cref="EvaluationOptions.JudgeModel"/> for the one grading it.
    /// </param>
    /// <returns>
    /// A client owned by this fixture and disposed with it, so the caller keeps it for the test and
    /// disposes nothing.
    /// </returns>
    public IChatClient ResolveChatClient(string model) =>
        Provider.GetRequiredKeyedService<IChatClient>(model);

    /// <summary>
    /// Disposes the container, and with it every chat client and agent resolved from it.
    /// </summary>
    /// <remarks>
    /// Asynchronous because the container's own teardown is: the chat clients underneath it are
    /// registered as transients, so it tracks each one it handed out and releases them here —
    /// through <see cref="IAsyncDisposable"/> where a service offers it. xUnit awaits this after
    /// the last test in the class has finished, and there is nothing to do in a run that resolved
    /// nothing.
    /// </remarks>
    public ValueTask DisposeAsync() => _provider?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// The container behind both resolutions, built the first time one is asked for.
    /// </summary>
    /// <remarks>
    /// Building is what starts owning something — sockets, an
    /// <see cref="IHttpClientFactory"/>, every transient it hands out — so it waits for a test that
    /// wants the system rather than a class that merely declares the fixture. What it is built from
    /// is not deferred: the constructor settled the registrations.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// There is no API key, so nothing was registered and there is no system to measure.
    /// </exception>
    private ServiceProvider Provider
    {
        get
        {
            // The production registration rejects a blank key, but from inside Agents.Api the
            // message names AgentChatOptions — a setting nobody configures when running a suite.
            // Say which knob to turn instead.
            if (string.IsNullOrWhiteSpace(EvaluationEnvironment.Current.ApiKey))
            {
                throw new InvalidOperationException(
                    $"No API key for {EvaluationEnvironment.Current.BaseUrl}. "
                    + "Set EvaluationOptions__ApiKey, either as an environment variable or with "
                    + "`dotnet user-secrets set EvaluationOptions:ApiKey \"...\" "
                    + "--project tests/Agents.Evals.Infrastructure`.");
            }

            return _provider ??= _services.BuildServiceProvider();
        }
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
