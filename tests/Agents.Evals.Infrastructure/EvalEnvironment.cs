using System.ComponentModel.DataAnnotations;
using Agents.Evals.Infrastructure.Probabilistic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Binds <see cref="EvalOptions"/> once for the test process and hands it to both suites.
/// </summary>
/// <remarks>
/// <para>
/// Sources are layered lowest → highest precedence: the <see cref="Defaults"/> table, then User
/// Secrets (local-only, opt-in via <c>dotnet user-secrets set Eval:ApiKey &lt;VALUE&gt;</c>), then
/// environment variables. The endpoint this repository points at is Azure OpenAI, which needs an
/// API key, and a key does not belong in shell history or in source control — User Secrets is where
/// it goes. Environment variables still win, so CI and scripted invocations behave the way an
/// operator expects.
/// </para>
/// <para>
/// Every value is defined by <see cref="EvalOptions"/> and defaulted by <see cref="Defaults"/>. The
/// environment is a provider feeding that section under the standard <c>Eval__Knob</c> spelling, not
/// a parallel set of knobs — so a value has one name, one type and one default whichever way it
/// arrives.
/// </para>
/// <para>
/// Because the secrets store belongs to <em>this</em> assembly, one
/// <c>dotnet user-secrets set --project tests/Agents.Evals.Infrastructure</c> configures both
/// suites rather than each needing its own copy of the same key.
/// </para>
/// </remarks>
public static class EvalEnvironment
{
    /// <summary>
    /// The default deployment and endpoint, matching <c>Agents.Api</c>'s own
    /// <c>appsettings.json</c>, so an unconfigured run measures the deployment the API actually
    /// uses.
    /// </summary>
    private const string DefaultModel = "gpt-4.1-dz-1";

    private const string DefaultBaseUrl = "https://shared-openai.openai.azure.com";

    /// <summary>
    /// The whole default policy, as configuration rather than as scattered <c>??</c> fallbacks, so
    /// the defaults are visible in one table and a missing one fails validation instead of quietly
    /// resolving to <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The two directory defaults sit beside the running test binary; the execution name is stamped
    /// here so every scenario in one run shares it.
    /// </remarks>
    private static Dictionary<string, string?> Defaults =>
        new()
        {
            [Key(nameof(EvalOptions.LiveModelEnabled))] = "false",
            [Key(nameof(EvalOptions.Model))] = DefaultModel,
            [Key(nameof(EvalOptions.BaseUrl))] = DefaultBaseUrl,
            [Key(nameof(EvalOptions.SampleSize))] = "35",
            [Key(nameof(EvalOptions.QualityFloor))] = "3.0",
            [Key(nameof(EvalOptions.CacheTimeToLive))] = "14.00:00:00",
            [Key(nameof(EvalOptions.ReportFormat))] = nameof(EvalReportFormat.All),
            [Key(nameof(EvalOptions.StorageRoot))] = Path.Combine(AppContext.BaseDirectory, "eval-store"),
            [Key(nameof(EvalOptions.ReportDirectory))] = Path.Combine(AppContext.BaseDirectory, "eval-reports"),
            [Key(nameof(EvalOptions.ExecutionName))] = $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
        };

    /// <summary>
    /// The bound, validated options for this test process.
    /// </summary>
    /// <remarks>
    /// Resolved once: static initialization runs on first access, under the CLR's type initializer,
    /// so both suites read the same instance and the timestamped
    /// <see cref="EvalOptions.ExecutionName"/> cannot drift between scenarios.
    /// </remarks>
    public static EvalOptions Current { get; } = Bind(BuildOperatorConfiguration());

    /// <summary>
    /// Layers <paramref name="source"/> over the <see cref="Defaults"/>, binds the
    /// <see cref="EvalOptions.SectionName"/> section and validates the result.
    /// </summary>
    /// <param name="source">
    /// Where the operator's values come from — User Secrets and the environment.
    /// </param>
    /// <exception cref="OptionsValidationException">
    /// A knob is missing, malformed or out of range. Failing here matters more than failing later:
    /// a suite that runs with a nonsensical floor or sample size still reports a verdict, and the
    /// verdict is meaningless.
    /// </exception>
    private static EvalOptions Bind(IConfiguration source)
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(Defaults)
            .AddConfiguration(source)
            .Build()
            .GetSection(EvalOptions.SectionName)
            .Get<EvalOptions>() ?? new EvalOptions();

        // The judge follows the deployment under test unless it is named, and has to follow the
        // one actually configured — which is knowable only after binding.
        if (string.IsNullOrWhiteSpace(options.JudgeModel))
        {
            options = options with { JudgeModel = options.Model };
        }

        return Validate(options);
    }

    /// <remarks>
    /// <see cref="ConfigurationExtensions.AddUserSecrets(IConfigurationBuilder, System.Reflection.Assembly, bool, bool)"/>
    /// is called with <c>optional: true</c> so a machine that has never run
    /// <c>dotnet user-secrets set</c> for this project — the common case, and the only case in CI —
    /// never throws building this.
    /// </remarks>
    private static IConfiguration BuildOperatorConfiguration() =>
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(EvalEnvironment).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static EvalOptions Validate(EvalOptions options)
    {
        var failures = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, new ValidationContext(options), failures, validateAllProperties: true))
        {
            return options;
        }

        throw new OptionsValidationException(
            EvalOptions.SectionName,
            typeof(EvalOptions),
            failures.Select(Describe));
    }

    /// <summary>
    /// States the failure and names the variable that fixes it, since the member name alone
    /// ("Model") does not tell an operator what to set.
    /// </summary>
    private static string Describe(ValidationResult failure)
    {
        var member = failure.MemberNames.FirstOrDefault();

        return member is null
            ? failure.ErrorMessage ?? $"{nameof(EvalOptions)} is invalid."
            : $"{failure.ErrorMessage} Set {EvalOptions.SectionName}__{member} (or {Key(member)} in user secrets).";
    }

    /// <summary>The configuration key a knob binds from, e.g. <c>Eval:Model</c>.</summary>
    private static string Key(string name) => $"{EvalOptions.SectionName}:{name}";
}
