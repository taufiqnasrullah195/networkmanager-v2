using System;
using System.IO;
using System.Linq;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Providers.Authentication;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tools;

namespace NETworkManager;

/// <summary>
///     Manual composition root for the AI copilot. Follows the codebase's manual-DI convention and assembles the
///     Step 3–7 pipeline with a <see cref="ToolActivityNotifier"/> so the UI can show live tool activity.
///     Provider selection goes through <see cref="ProviderRegistry"/>: a configured Custom Agent is used when
///     available (endpoint + credential from the DPAPI-backed secure store), otherwise the deterministic mock
///     provider serves as the development placeholder. No secret is ever stored in the configuration file — it
///     references the credential key only.
/// </summary>
public static class AICopilotFactory
{
    private const string CredentialKey = "custom-agent";

    /// <summary>Data directory for AI configuration and credentials (per-user, local application data).</summary>
    private static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NETworkManager",
            "AI");

    public static AICopilotSession CreateSession()
    {
        // Configuration (no secrets) + secure credential store (DPAPI).
        var configurationStore = new FileCopilotConfigurationStore(Path.Combine(DataDirectory, "copilot-provider.json"));
        var configuration = configurationStore.Load();
        var credentialStore = new DpapiSecureCredentialStore(Path.Combine(DataDirectory, "credentials"));

        // Tool layer (Step 3) + diagnostic layer (Step 6) + control layer (Step 5) + activity events.
        var notifier = new ToolActivityNotifier();

        var registry = new ToolRegistry();
        foreach (var tool in NetworkToolCollection.All())
            registry.Register(tool);

        var execution = new ToolExecutionService(registry);
        var diagnostics = new DiagnosticEngine(execution, notifier);
        registry.Register(new InternetConnectivityDiagnosticTool(diagnostics));

        // Expose the Step 9 monitoring state to the copilot as a read-only tool (no mutation path).
        registry.Register(new MonitoringStatusTool(MonitoringComposition.Instance));

        var orchestrator = new ToolCallOrchestrator(registry, execution, logger: notifier);

        // Provider selection (Step 4 registry): Custom Agent when configured, mock otherwise.
        var providerRegistry = new ProviderRegistry();
        var providerInfo = "Provider: Mock (development)";

        if (configuration is { IsConfigured: true } && configuration.Validate().Count == 0)
        {
            var provider = BuildCustomAgentProvider(configuration, credentialStore);

            if (provider is not null)
            {
                providerRegistry.Register("custom-agent", provider);
                providerInfo = "Provider: Custom Agent";
            }
        }

        providerRegistry.Register("mock", new MockAIProvider());
        providerRegistry.Select(providerRegistry.Names.Contains("custom-agent") ? "custom-agent" : "mock");

        var loop = new AgentToolLoop(providerRegistry.Resolve(), orchestrator, registry, logger: notifier);
        var service = new AIConversationService(loop, new DefaultDiagnosticAnalyzer());

        return new AICopilotSession(
            new CopilotController(service, notifier),
            providerInfo,
            configuration,
            configurationStore,
            credentialStore);
    }

    /// <summary>Builds the real provider from runtime configuration. Returns null when a credential is required but missing.</summary>
    internal static CustomAgentProvider? BuildCustomAgentProvider(
        CopilotProviderConfiguration configuration,
        ISecureCredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credentialStore);

        if (!configuration.IsConfigured)
            return null;

        IAgentAuthentication? authentication = configuration.AuthenticationMode switch
        {
            "api-key" => new ApiKeyAuthentication(() => credentialStore.GetAsync(CredentialKey).GetAwaiter().GetResult() ?? string.Empty),
            "bearer" => new BearerTokenAuthentication(() => credentialStore.GetAsync(CredentialKey).GetAwaiter().GetResult() ?? string.Empty),
            _ => null,
        };

        return new CustomAgentProvider(configuration.ToCustomAgentConfiguration(), authentication);
    }
}

/// <summary>Everything the copilot view needs: controller, provider status, and runtime configuration handles.</summary>
public sealed record AICopilotSession(
    CopilotController Controller,
    string ProviderInfo,
    CopilotProviderConfiguration? Configuration,
    FileCopilotConfigurationStore ConfigurationStore,
    ISecureCredentialStore CredentialStore);