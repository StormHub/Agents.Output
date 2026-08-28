using System.Globalization;

namespace Agents.Evals.Infrastructure.Probabilistic;

/// <summary>
/// How often one check passed across a sample of agent runs.
/// </summary>
/// <param name="CheckName">The check's metric name.</param>
/// <param name="Passed">Runs in which the check passed.</param>
/// <param name="Total">Runs in the sample.</param>
public sealed record CheckRate(string CheckName, int Passed, int Total)
{
    /// <summary>Two-sided 95% confidence (z for 0.975).</summary>
    private const double Z = 1.959963985;

    /// <summary>The pass rate actually observed in this sample.</summary>
    public double Observed => Total == 0 ? 0 : (double)Passed / Total;

    /// <summary>
    /// Lower bound of the 95% Wilson score interval for the true pass rate.
    /// </summary>
    /// <remarks>
    /// Gating on this rather than on <see cref="Observed"/> is what makes a small sample honest.
    /// Five out of five runs looks like a 100% pass rate but is consistent with a true rate as
    /// low as 57%, and the lower bound says so. Wilson is used instead of the normal
    /// approximation because it stays well-behaved at small <see cref="Total"/> and at rates near
    /// 0 or 1, which is exactly where agent evaluation lives.
    /// </remarks>
    public double LowerBound
    {
        get
        {
            if (Total == 0)
            {
                return 0;
            }

            var p = Observed;
            var n = (double)Total;
            const double ZSquared = Z * Z;

            var centre = p + (ZSquared / (2 * n));
            var margin = Z * Math.Sqrt((p * (1 - p) / n) + (ZSquared / (4 * n * n)));
            var denominator = 1 + (ZSquared / n);

            return Math.Max(0, (centre - margin) / denominator);
        }
    }

    public string Describe() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}: {1}/{2} = {3:P1} (95% lower bound {4:P1})",
        CheckName,
        Passed,
        Total,
        Observed,
        LowerBound);
}
