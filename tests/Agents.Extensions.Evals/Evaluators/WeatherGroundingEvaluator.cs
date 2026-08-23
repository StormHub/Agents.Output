using System.Globalization;
using System.Text.RegularExpressions;
using Agents.Evals.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Agents.Extensions.Evals.Evaluators;

/// <summary>
/// Checks that every number the answer reports as a weather reading came out of a tool.
/// </summary>
/// <remarks>
/// <para>
/// This is the domain rule the library cannot ship: inventing a temperature is the specific
/// failure this pipeline exists to avoid, and no general-purpose evaluator knows that. Writing it
/// as an <see cref="IEvaluator"/> rather than as a bare assertion is what buys the rest of the
/// machinery — it composes with the shipped evaluators, its metric lands in the same
/// <c>EvaluationResult</c>, and the report treats it like any other.
/// </para>
/// <para>
/// It is also the only evaluator here that needs neither a model nor a reference answer, so it
/// costs nothing and can gate every run.
/// </para>
/// </remarks>
internal sealed class WeatherGroundingEvaluator : IEvaluator
{
    /// <summary>Name of the metric this evaluator produces.</summary>
    public const string GroundedWeatherClaimMetricName = "Grounded Weather Claim";

    /// <summary>Name of the context recording what the answer was checked against.</summary>
    public const string ToolReadingsContextName = "Tool readings";

    /// <summary>
    /// How far a claimed value may sit from a tool value and still count as grounded. Models
    /// round — "22°C" for a reading of 22.4 is reporting, not inventing.
    /// </summary>
    private const double Tolerance = 0.5;

    /// <summary>
    /// A number is treated as a weather claim only when a unit follows it, so "a 7-day forecast"
    /// is prose and "22.4°C" is a reading.
    /// </summary>
    private static readonly Regex ClaimPattern = new(
        @"(-?\d+(?:\.\d+)?)\s*(?:°|º|degrees|%|percent|km/h|kmh|mph|mm)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } = [GroundedWeatherClaimMetricName];

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var readings = ToolResults.Values(messages, modelResponse);
        var claims = ClaimedValues(modelResponse.Text);

        var metric = new BooleanMetric(GroundedWeatherClaimMetricName);

        if (claims.Count == 0)
        {
            metric.Value = true;
            metric.Reason = "The answer reports no readings, so there is nothing to ground.";
        }
        else if (readings.Count == 0)
        {
            metric.Value = false;
            metric.Reason =
                $"The answer reports {Describe(claims)} but no tool was called — those numbers came "
                + "from the model's weights.";
        }
        else
        {
            var ungrounded = claims
                .Where(claim => !readings.Any(reading => Math.Abs(reading - claim) <= Tolerance))
                .ToList();

            metric.Value = ungrounded.Count == 0;
            metric.Reason = ungrounded.Count == 0
                ? $"Every reported value ({Describe(claims)}) appears in a tool result."
                : $"{Describe(ungrounded)} appears in the answer but in no tool result.";
        }

        metric.Interpretation = new EvaluationMetricInterpretation(
            metric.Value is true ? EvaluationRating.Exceptional : EvaluationRating.Unacceptable,
            failed: metric.Value is not true,
            reason: metric.Reason);

        // Recording what the check compared against is what makes a red metric diagnosable from
        // the report alone, without re-running the scenario.
        metric.AddOrUpdateContext(
            new EvaluationContext(
                ToolReadingsContextName,
                readings.Count == 0 ? "(no tool results in this turn)" : Describe(readings)));

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    private static IReadOnlyList<double> ClaimedValues(string answer)
    {
        var claims = new List<double>();

        foreach (Match match in ClaimPattern.Matches(answer))
        {
            if (double.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                && !claims.Contains(value))
            {
                claims.Add(value);
            }
        }

        return claims;
    }

    private static string Describe(IEnumerable<double> values) =>
        string.Join(", ", values.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)));
}
