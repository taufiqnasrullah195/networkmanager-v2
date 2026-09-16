namespace NETworkManager.AI.Conversation;

/// <summary>
///     Orchestrates a network-diagnostics conversation: accepts user input, maintains in-memory conversation context,
///     runs the provider/android tool loop, triggers diagnostic workflows, and returns a structured, evidence-backed
///     <see cref="AIAnalysisResponse"/>. It never performs networking itself and never calls an AI provider directly —
///     all provider interaction and tool execution goes through the injected orchestrator/loop.
/// </summary>
public interface IAIConversationService
{
    Task<AIConversationResult> SendAsync(string userMessage, string? conversationId = null, CancellationToken cancellationToken = default);
}