using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Pulls the tool results out of a completed turn.
/// </summary>
/// <remarks>
/// <para>
/// This is the grounding truth for the whole suite. <c>UseFunctionInvocation</c> runs the tool loop
/// inside <see cref="IChatClient.GetResponseAsync"/> and appends every intermediate message — the
/// assistant's tool call, the tool's result, the final answer — to
/// <see cref="ChatResponse.Messages"/>. So the record of what the model was actually told sits in
/// the response, not in the request, and both are searched here.
/// </para>
/// <para>
/// <see cref="Render"/> produces the text handed to a groundedness evaluator; <see cref="Values"/>
/// produces the numbers a grounding check compares an answer's readings against.
/// </para>
/// </remarks>
public static class ToolResults
{
    private static readonly Regex NumberPattern = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>
    /// Renders every tool result in the turn as text, for evaluators that take a grounding context.
    /// </summary>
    public static string Render(IEnumerable<ChatMessage> messages, ChatResponse modelResponse)
    {
        var builder = new StringBuilder();

        foreach (var content in Contents(messages, modelResponse))
        {
            builder.Append(content.CallId).Append(": ").AppendLine(Serialize(content.Result));
        }

        return builder.Length == 0
            ? "No tool was called, so no external information was available to the model."
            : builder.ToString();
    }

    /// <summary>Every number that appears anywhere in a tool result.</summary>
    /// <remarks>
    /// Deliberately coarse: it takes coordinates and forecast entries as well as the current
    /// reading, so it admits anything the model could have read off a tool and rejects only numbers
    /// that appear nowhere in the turn. A stricter version would have to model each tool's payload,
    /// and would then start failing when a tool's shape changes rather than when the model invents
    /// a number.
    /// </remarks>
    public static IReadOnlyList<double> Values(IEnumerable<ChatMessage> messages, ChatResponse modelResponse)
    {
        var values = new List<double>();

        foreach (var content in Contents(messages, modelResponse))
        {
            foreach (Match match in NumberPattern.Matches(Serialize(content.Result)))
            {
                if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static IEnumerable<FunctionResultContent> Contents(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse) =>
            messages
                .Concat(modelResponse.Messages)
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>();

    private static string Serialize(object? result) =>
        result as string ?? JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
}
