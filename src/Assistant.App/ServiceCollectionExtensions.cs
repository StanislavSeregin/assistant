using Assistant.App.Bootstrap;
using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Support;
using Assistant.App.Tools;
using Assistant.App.UI;
using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Formatting;
using Assistant.App.UI.Tui;
using Assistant.App.UI.Tui.Log;
using Assistant.App.UI.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.App;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAssistantCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .Configure<Settings>(configuration.GetSection("Settings"))
            .AddSingleton<ChatClientFactory>()
            .AddSingleton<LifecycleEventChannel>()
            .AddSingleton<ILifecycleSink>(sp => sp.GetRequiredService<LifecycleEventChannel>())
            .AddSingleton<IAgentStateStore, LiteDbAgentStateStore>()
            .AddSingleton<SessionCheckpoint>()
            .AddSingleton<NodeRegistry>()
            .AddSingleton<MailService>()
            .AddSingleton<ModelSlotLimiter>()
            .AddSingleton<AgentBootstrap>()
            .AddSingleton<AgentMailTools>()
            .AddSingleton<StatelessAgent>()
            .AddSingleton<TurnSupportAdvisor>()
            .AddSingleton<TurnRunner>()
            .AddHostedService<LifecycleEventService>()
            .AddHostedService<UserBootstrapHostedService>()
            .AddHostedService<LlmNodeScheduler>();

        return services;
    }

    /// <summary>
    /// Terminal.Gui tiled UI. Swap this registration for another toolkit that implements
    /// <see cref="ILogSink"/> / <see cref="IUserWorkspace"/> / <see cref="IAppUi"/>.
    /// </summary>
    public static IServiceCollection AddTerminalGuiUi(this IServiceCollection services)
    {
        services
            .AddSingleton<LogBuffer>()
            .AddSingleton<TuiUiScheduler>()
            .AddSingleton<IUiScheduler>(sp => sp.GetRequiredService<TuiUiScheduler>())
            .AddSingleton<CoalescingLogSink>()
            .AddSingleton<ILogSink>(sp => sp.GetRequiredService<CoalescingLogSink>())
            .AddSingleton<LifecycleLogPresenter>()
            .AddSingleton<UserWorkspace>()
            .AddSingleton<IUserWorkspace>(sp => sp.GetRequiredService<UserWorkspace>())
            .AddSingleton<ILifecycleEventHandler, TuiLifecycleOutput>()
            .AddSingleton<TuiAppUi>()
            .AddSingleton<IAppUi>(sp => sp.GetRequiredService<TuiAppUi>());

        return services;
    }
}
