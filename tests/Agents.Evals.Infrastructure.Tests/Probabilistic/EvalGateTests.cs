using Agents.Evals.Infrastructure.Probabilistic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace Agents.Evals.Infrastructure.Tests.Probabilistic;

/// <summary>
/// Tests for the rate and gating arithmetic.
/// </summary>
/// <remarks>
/// Unlike the live suite these are genuine deterministic tests — the statistics are fixed
/// functions of the counts. If this arithmetic is wrong the live suite silently gates on
/// nonsense, so it gets the strictest treatment in the repository.
/// </remarks>
public sealed class EvalGateTests
{
    [Fact]
    public void SmallPerfectSample_DoesNotClaimCertainty()
    {
        // Five out of five looks like 100% but is consistent with a true rate near 57%.
        // Gating on the observed rate would read this as proof; the bound refuses to.
        var rate = new CheckRate("check", 5, 5);

        Assert.Equal(1.0, rate.Observed);
        Assert.InRange(rate.LowerBound, 0.56, 0.57);
    }

    [Fact]
    public void LowerBound_TightensAsTheSampleGrows()
    {
        var small = new CheckRate("check", 10, 10);
        var large = new CheckRate("check", 100, 100);

        Assert.True(small.LowerBound < large.LowerBound);
        Assert.True(large.LowerBound < 1.0);
    }

    [Fact]
    public void EmptySample_ScoresZero()
    {
        var rate = new CheckRate("check", 0, 0);

        Assert.Equal(0d, rate.Observed);
        Assert.Equal(0d, rate.LowerBound);
    }

    [Theory]
    [InlineData(0.80, 16)]
    [InlineData(0.90, 35)]
    public void MinimumSampleFor_MatchesTheClosedForm(double floor, int expected)
    {
        // For a flawless sample the Wilson bound reduces to n / (n + z squared).
        Assert.Equal(expected, EvalGate.MinimumSampleFor(floor));
    }

    [Fact]
    public void RateFloor_ToleratesAnOccasionalMiss()
    {
        var floors = Floors(("routing", new CheckFloor(0.80, "rate")));

        // 29/30 has a lower bound of ~0.833 — comfortably clear of an 80% floor.
        var outcome = EvalGate.Evaluate([new CheckRate("routing", 29, 30)], floors);

        Assert.True(outcome.Passed, outcome.Report());
    }

    [Fact]
    public void RateFloor_FailsOnASustainedDrop()
    {
        var floors = Floors(("routing", new CheckFloor(0.80, "rate")));

        // 28/30 drops the lower bound to ~0.787.
        var outcome = EvalGate.Evaluate([new CheckRate("routing", 28, 30)], floors);

        Assert.False(outcome.Passed);
        Assert.Contains("routing", Assert.Single(outcome.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void Invariant_GetsNoStatisticalBenefitOfTheDoubt()
    {
        var floors = Floors(("safety", new CheckFloor(1.0, "defect")));

        // The same 29/30 that clears an 80% rate floor fails an invariant outright.
        var outcome = EvalGate.Evaluate([new CheckRate("safety", 29, 30)], floors);

        Assert.False(outcome.Passed);
        Assert.Contains("invariant violated", Assert.Single(outcome.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void Invariant_PassesWhenNeverViolated()
    {
        var floors = Floors(("safety", new CheckFloor(1.0, "defect")));

        var outcome = EvalGate.Evaluate([new CheckRate("safety", 30, 30)], floors);

        Assert.True(outcome.Passed, outcome.Report());
    }

    [Fact]
    public void MissingCheck_IsAViolationRatherThanASilentPass()
    {
        // A floor naming a check the evaluator never emits — a typo, or a renamed check — must
        // not read as success, or the gate looks stronger than it is.
        var floors = Floors(("typo_in_name", new CheckFloor(0.80, "rate")));

        var outcome = EvalGate.Evaluate([new CheckRate("routing", 30, 30)], floors);

        Assert.False(outcome.Passed);
        Assert.Contains("never produced", Assert.Single(outcome.Violations), StringComparison.Ordinal);
    }

    [Fact]
    public void UngatedChecks_AreReportedButNotJudged()
    {
        var floors = Floors(("routing", new CheckFloor(0.80, "rate")));

        var outcome = EvalGate.Evaluate(
            [new CheckRate("routing", 30, 30), new CheckRate("observed_only", 1, 30)],
            floors);

        Assert.True(outcome.Passed, outcome.Report());
        Assert.Contains("observed_only", outcome.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public void PerCheck_SplitsAggregateResultsByCheck()
    {
        var results = new AgentEvaluationResults(
            "test",
            [
                ResultWith(("routing", true), ("safety", true)),
                ResultWith(("routing", false), ("safety", true)),
                ResultWith(("routing", true), ("safety", true)),
            ]);

        var rates = EvalRates.PerCheck(results).ToDictionary(rate => rate.CheckName, StringComparer.Ordinal);

        Assert.Equal(2, rates["routing"].Passed);
        Assert.Equal(3, rates["routing"].Total);
        Assert.Equal(3, rates["safety"].Passed);

        // The aggregate hides which check failed — that is the reason PerCheck exists.
        Assert.Equal(2, EvalRates.Overall(results).Passed);
    }

    private static Dictionary<string, CheckFloor> Floors(params (string Name, CheckFloor Floor)[] floors) =>
        floors.ToDictionary(entry => entry.Name, entry => entry.Floor, StringComparer.Ordinal);

    private static EvaluationResult ResultWith(params (string Name, bool Passed)[] checks)
    {
        var result = new EvaluationResult();

        foreach (var (name, passed) in checks)
        {
            result.Metrics[name] = new BooleanMetric(name, passed)
            {
                Interpretation = new EvaluationMetricInterpretation
                {
                    Rating = passed ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                    Failed = !passed,
                },
            };
        }

        return result;
    }
}
