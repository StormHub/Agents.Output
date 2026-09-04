using System.Collections.Concurrent;
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
/// otherwise restate the same three steps: project <see cref="EvalOptions"/> onto
/// <c>AgentChatOptions</c>, run production's registration, resolve. Doing it here once means the
/// suites measure the client the API builds — same transport, same options — rather than a
/// lookalike assembled per suite that could drift from it or from each other.
/// </para>
/// <para>
/// What each suite pulls out of the container differs, which is why this returns the provider rather
/// than a client: the trajectory suite resolves the <c>ChatClientAgent</c>, the metrics suite
/// resolves the keyed <c>IChatClient</c> underneath it.
/// </para>
/// <para>
/// Take it as a class fixture — <c>IClassFixture&lt;EvaluationSetup&gt;</c> — and let it hand out the
/// containers:
/// </para>
/// <code>
/// public sealed class MyEvalTests(EvaluationSetup setup) : IClassFixture&lt;EvaluationSetup&gt;
/// {
///     [Fact]
///     public async Task Measures()
///     {
///         var agent = setup.ForLiveModel(EvalEnvironment.Current.Model).GetRequiredService&lt;ChatClientAgent&gt;();
///         // ...
///     }
/// }
/// </code>
/// <para>
/// The containers are built inside the fixture and disposed with it, once the last test in the
/// class has run. That scoping is the point: a container held statically for the life of the test
/// process outlives every suite that used it, keeps its sockets and its
/// <see cref="IHttpClientFactory"/> alive long after the measurements are over, and leaves a failed
/// run's state visible to the next one. Per class, the deployment a suite talks to is set up and
/// torn down with that suite, and nothing a suite builds leaks into another.
/// </para>
/// <para>
/// The cost of that scoping is that two test classes pointed at the same deployment each build their
/// own container and their own connection pool. That is a handful of extra handshakes against an
/// endpoint the suites are about to make hundreds of model calls to — a rounding error next to
/// knowing when the thing is disposed.
/// </para>
/// </remarks>
public sealed class EvaluationSetup : IAsyncDisposable
{
    /// <summary>
    /// One container per (deployment, tool choice), for the life of the fixture.
    /// </summary>
    /// <remarks>
    /// A single class routinely needs more than one: the metrics suite points the system under test
    /// and the judge at different deployments, and reuse within the class lets them share a
    /// connection pool when those deployments are the same. A failed build is not cached:
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey, TValue})"/> does not
    /// store an entry when the factory throws, so a run that failed on a missing key can be retried
    /// once it is set.
    /// </remarks>
    private readonly ConcurrentDictionary<(string Model, bool WithProductionTools), ServiceProvider> _providers = new();

    private bool _disposed;

    /// <summary>
    /// Builds (or reuses, within this fixture) a container wired to <paramref name="model"/> at
    /// <see cref="EvalOptions.BaseUrl"/>.
    /// </summary>
    /// <param name="model">The deployment to point at — the system under test, or a judge.</param>
    /// <param name="withProductionTools">
    /// <see langword="true"/> registers the real <c>CalendarDay</c> and <c>WeatherForecast</c>
    /// tools, which call Open-Meteo. <see langword="false"/> — the default — leaves the tool
    /// collection empty so the caller can supply stubbed tools instead and keep the readings fixed
    /// and knowable.
    /// </param>
    /// <returns>
    /// A container owned by this fixture. Everything resolved from it is disposed when the fixture
    /// is, so a caller should not dispose what it resolves.
    /// </returns>
    public IServiceProvider ForLiveModel(string model, bool withProductionTools = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The production registration rejects a blank key, but from inside Agents.Api the message
        // names AgentChatOptions — a setting nobody configures when running a suite. Say which knob
        // to turn instead.
        if (string.IsNullOrWhiteSpace(EvalEnvironment.Current.ApiKey))
        {
            throw new InvalidOperationException(
                $"No API key for {EvalEnvironment.Current.BaseUrl}. Set Eval__ApiKey, either as an "
                + "environment variable or with `dotnet user-secrets set Eval:ApiKey \"...\" "
                + "--project tests/Agents.Evals.Infrastructure`.");
        }

        return _providers.GetOrAdd((model, withProductionTools), static key =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["AgentChatOptions:Model"] = key.Model,
                        ["AgentChatOptions:BaseUrl"] = EvalEnvironment.Current.BaseUrl,
                        ["AgentChatOptions:ApiKey"] = EvalEnvironment.Current.ApiKey,
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

    /// <summary>
    /// Disposes every container this fixture built, and with them every chat client and agent
    /// resolved from one.
    /// </summary>
    /// <remarks>
    /// Asynchronous because a container's own teardown is: the chat clients underneath it are
    /// registered as transients, so the provider tracks each one it handed out and releases them
    /// here — through <see cref="IAsyncDisposable"/> where a service offers it. xUnit awaits this
    /// after the last test in the class has finished.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var provider in _providers.Values)
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }

        _providers.Clear();
    }
}
