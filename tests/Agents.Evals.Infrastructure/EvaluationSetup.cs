using Agents.Api.Options;
using Agents.Api.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Class fixture holding the registrations that compose the live system under evaluation, out of
/// <c>Agents.Api</c>'s own DI.
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
/// This is the setup, not the container: it owns a <see cref="ServiceCollection"/> and nothing
/// else. <see cref="Build"/> hands a caller its own container, and the caller owns it from there —
/// which is why this fixture is not disposable. A test class builds one in its constructor and
/// disposes it when it is done, so the sockets and the <see cref="IHttpClientFactory"/> behind a
/// measurement live exactly as long as the test that made it, and no run inherits the state of the
/// one before it.
/// </para>
/// <code>
/// public sealed class MyEvalTests(EvaluationSetup setup)
///     : IClassFixture&lt;EvaluationSetup&gt;, IAsyncDisposable
/// {
///     private readonly ServiceProvider _provider = setup.Build();
///
///     public ValueTask DisposeAsync() => _provider.DisposeAsync();
///
///     [Fact]
///     public async Task Measures()
///     {
///         var agent = _provider.GetRequiredService&lt;ChatClientAgent&gt;();
///         // ...
///     }
/// }
/// </code>
/// <para>
/// What each suite pulls out of its container differs, which is why <see cref="Build"/> returns the
/// container rather than a client: the trajectory suite resolves the <c>ChatClientAgent</c>, the
/// metrics suite the keyed <c>IChatClient</c> underneath it and, where a judge is configured
/// separately, a second keyed client beside it.
/// </para>
/// </remarks>
public sealed class EvaluationSetup
{
    private readonly ServiceCollection _services = new();

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
    /// Nothing is registered at all when the live tiers are off. xUnit builds this fixture, and the
    /// test class that holds it, before either can skip — so an offline run, which is the common
    /// case and the only case in CI, has to be able to build an empty container and skip quietly
    /// rather than fail a whole class from a constructor.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The live tiers are on with no API key. Production's registration rejects a blank key where
    /// it is registered rather than where it is used, so this is the first point the suites can
    /// say which knob to turn — and saying it here fails the class that asked for a live run,
    /// rather than the offline runs that never wanted one.
    /// </exception>
    public EvaluationSetup()
    {
        var options = EvaluationEnvironment.Current;

        _services.AddLogging();

        if (!options.LiveModelEnabled)
        {
            return;
        }

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

        _services.AddTools();

        // A judge pointed at its own deployment needs its own keyed client, which is what a second
        // pass over the registration adds. It goes first so the system under test is the last
        // AgentChatOptions registered: that singleton is the one production's ChatClientAgent
        // resolves, and the agent a suite measures has to be the deployment under evaluation, never
        // the one grading it. Skipped when the judge follows the model — the default — where one
        // keyed client already serves both.
        if (!string.Equals(options.JudgeModel, options.Model, StringComparison.Ordinal))
        {
            _services.AddWeatherChatAgent(Project(options, options.JudgeModel));
        }

        _services.AddWeatherChatAgent(Project(options, options.Model));
    }

    /// <summary>
    /// Builds a container from these registrations, for the caller to own and dispose.
    /// </summary>
    /// <remarks>
    /// One per test class instance — which xUnit creates per test — so a measurement's transport is
    /// set up and torn down with the test that made it. Disposing the returned container releases
    /// every chat client and agent resolved from it, so a test disposes that and nothing else.
    /// </remarks>
    public ServiceProvider Build() => _services.BuildServiceProvider();

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
