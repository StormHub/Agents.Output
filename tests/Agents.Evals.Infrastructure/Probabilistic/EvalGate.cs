using System.Globalization;

namespace Agents.Evals.Infrastructure.Probabilistic;

/// <summary>
/// The floor a single check has to clear.
/// </summary>
/// <remarks>
/// A floor of <c>1.0</c> means the check is an <em>invariant</em>: any failure is a defect, and
/// the gate gives it no statistical benefit of the doubt. Anything below <c>1.0</c> means the
/// check is a <em>rate</em>: the agent is allowed to miss sometimes, and the gate asks whether
/// the sample gives 95% confidence that the true rate clears the floor.
/// </remarks>
/// <param name="Floor">Minimum acceptable pass rate, in [0, 1].</param>
/// <param name="Rationale">Why this check gets this floor. Shown in failure output.</param>
public sealed record CheckFloor(double Floor, string Rationale)
{
    public bool IsInvariant => Floor >= 1.0;
}

/// <summary>
/// Evaluates measured pass rates against their floors.
/// </summary>
/// <remarks>
/// Every check is judged before anything is reported, so a failing run prints the whole picture
/// rather than stopping at the first violation. That matters more for evaluation than for tests:
/// the interesting signal is usually the shape of the failures, not the first one.
/// </remarks>
public static class EvalGate
{
    /// <summary>
    /// Returns the checks that missed their floor, plus a report of every measured rate.
    /// </summary>
    public static GateOutcome Evaluate(
        IReadOnlyList<CheckRate> rates,
        IReadOnlyDictionary<string, CheckFloor> floors)
    {
        var violations = new List<string>();
        var unmeasured = floors.Keys
            .Where(name => rates.All(rate => rate.CheckName != name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in unmeasured)
        {
            // A floor for a check the evaluator never emitted is a typo or a stale name, and
            // silently passing it would make the gate look stronger than it is.
            violations.Add($"{name}: no results — the evaluator never produced this check.");
        }

        foreach (var rate in rates)
        {
            if (!floors.TryGetValue(rate.CheckName, out var floor))
            {
                continue;
            }

            if (floor.IsInvariant)
            {
                if (rate.Passed < rate.Total)
                {
                    violations.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} — invariant violated in {1} of {2} run(s). {3}",
                        rate.Describe(),
                        rate.Total - rate.Passed,
                        rate.Total,
                        floor.Rationale));
                }

                continue;
            }

            if (rate.LowerBound < floor.Floor)
            {
                violations.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} — lower bound below floor {1:P0}. {2}",
                    rate.Describe(),
                    floor.Floor,
                    floor.Rationale));
            }
        }

        return new GateOutcome(rates, violations);
    }

    /// <summary>
    /// The smallest sample in which a flawless run can clear <paramref name="floor"/>.
    /// </summary>
    /// <remarks>
    /// Useful when choosing <c>numRepetitions</c>: gating on a lower bound is pointless if the
    /// sample is too small for even a perfect score to reach the floor. For a flawless sample the
    /// bound reduces to <c>n / (n + z²)</c>, so a 90% floor needs at least 35 runs and an 80%
    /// floor needs 16.
    /// </remarks>
    public static int MinimumSampleFor(double floor)
    {
        for (var n = 1; n <= 10_000; n++)
        {
            if (new CheckRate("probe", n, n).LowerBound >= floor)
            {
                return n;
            }
        }

        return -1;
    }
}

/// <summary>The result of judging a sample against its floors.</summary>
/// <param name="Rates">Every measured rate, whether gated or not.</param>
/// <param name="Violations">Human-readable descriptions of the checks that missed.</param>
public sealed record GateOutcome(IReadOnlyList<CheckRate> Rates, IReadOnlyList<string> Violations)
{
    public bool Passed => Violations.Count == 0;

    /// <summary>A full account of the run, suitable for an assertion message or a log.</summary>
    public string Report()
    {
        var lines = new List<string> { "Measured rates:" };
        lines.AddRange(Rates.Select(rate => $"  {rate.Describe()}"));

        if (Violations.Count > 0)
        {
            lines.Add($"Below floor ({Violations.Count}):");
            lines.AddRange(Violations.Select(violation => $"  {violation}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
