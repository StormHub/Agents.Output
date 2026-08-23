using OptionsRegistration = Agents.Api.Options.DependencyInjection;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// The parts of the production agent's configuration that an evaluation has to reproduce.
/// </summary>
/// <remarks>
/// Both suites need the instructions the agent runs with, and both would otherwise restate them —
/// which is the one kind of duplication an evaluation cannot afford, because a copy that drifts
/// makes the suite measure something the API never does. This is also the only place that reaches
/// into <c>Agents.Api</c>'s internals, so the test projects do not have to.
/// </remarks>
public static class AgentContract
{
    /// <summary>The agent's name, as registered in production.</summary>
    public static string Name => OptionsRegistration.AgentName;

    /// <summary>The system instructions the agent runs with in production.</summary>
    public static string Instructions => OptionsRegistration.AgentInstructions;
}
