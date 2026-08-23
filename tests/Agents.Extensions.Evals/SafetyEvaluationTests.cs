using Agents.Evals.Infrastructure;
using Agents.Extensions.Evals.Infrastructure;
using Azure.Identity;
using Microsoft.Extensions.AI.Evaluation.Safety;
using Xunit;

namespace Agents.Extensions.Evals;

/// <summary>
/// The safety tier: content harm and protected material, scored by the Azure AI Foundry
/// evaluation service.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is graded locally here. <c>ContentSafetyServiceConfiguration.ToChatConfiguration</c>
/// wraps the Foundry evaluation service in an <c>IChatClient</c>, which is how these evaluators
/// travel through the same <c>ScenarioRun</c> plumbing as the judged ones while talking to a
/// service instead of a model. Severities come back on a 0-to-7 scale where lower is better, and
/// the library's own interpretation — fail above 2 — is left in place.
/// </para>
/// <para>
/// A weather assistant is not where content harms are likely, which is the point: this is the
/// tier you keep green so that a change in instructions, a new tool, or a swapped model has
/// something to be measured against. Set <c>EVAL_SAFETY_ENDPOINT</c> to an Azure AI Foundry
/// project endpoint to enable it; the credential is <c>DefaultAzureCredential</c>, so
/// <c>az login</c> or a workload identity is enough.
/// </para>
/// </remarks>
public sealed class SafetyEvaluationTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "Content safety evaluation is off. Set EVAL_SAFETY_ENDPOINT to an Azure AI Foundry project endpoint to enable it.";

    [Fact]
    public async Task Answers_CarryNoContentHarms()
    {
        Assert.SkipUnless(EvalEnvironment.SafetyEnabled, SkipReason);

        var cancellationToken = TestContext.Current.CancellationToken;

        var serviceConfiguration = new ContentSafetyServiceConfiguration(
            credential: new DefaultAzureCredential(),
            endpoint: new Uri(EvalEnvironment.SafetyEndpoint!));

        var reporting = EvaluationReporting.ForSafetyChecks(serviceConfiguration.ToChatConfiguration());

        // The safety evaluators grade text, so the scripted pipeline is enough when no model is
        // available — the answers are then fixed, which is a wiring check rather than a
        // measurement. With EVAL_LIVE_MODEL=1 the real model's own words are graded instead.
        using var client = EvalEnvironment.LiveModelEnabled
            ? WeatherChatPipeline.CreateLive()
            : WeatherChatPipeline.CreateScripted([.. WeatherScenarios.All]);

        foreach (var scenario in WeatherScenarios.Grounded)
        {
            await using var scenarioRun = await reporting.CreateScenarioRunAsync(
                $"safety.{scenario.Name}",
                cancellationToken: cancellationToken);

            var (messages, response) = await WeatherChatPipeline.RunAsync(
                client,
                scenario.Query,
                cancellationToken);

            var result = await scenarioRun.EvaluateAsync(messages, response, cancellationToken: cancellationToken);

            EvaluationReporting.Report(output, scenario.Name, result);

            EvaluationReporting.AssertNoDiagnosticErrors(result);
            EvaluationReporting.AssertNoFailures(result);
        }
    }
}
