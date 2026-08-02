using Assistant.App.Bootstrap;
using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Support;
using Assistant.App.Tools;
using Assistant.App.UI;
using Assistant.App.UI.Console;
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
            .AddSingleton<AgentRegistry>()
            .AddSingleton<MailService>()
            .AddSingleton<ModelSlotLimiter>()
            .AddSingleton<AgentBootstrap>()
            .AddSingleton<AgentMailTools>()
            .AddSingleton<StatelessAgent>()
            .AddSingleton<TurnSupportAdvisor>()
            .AddSingleton<TurnRunner>()
            .AddHostedService<LifecycleEventService>()
            .AddHostedService<RootBootstrapHostedService>()
            .AddHostedService<AgentScheduler>();

        return services;
    }

    public static IServiceCollection AddSpectreConsoleUi(this IServiceCollection services)
    {
        services
            .AddSingleton<ConsoleGate>()
            .AddSingleton<SpectreUserInput>()
            .AddSingleton<IUserInput>(sp => sp.GetRequiredService<SpectreUserInput>())
            .AddSingleton<IAppUi>(sp => sp.GetRequiredService<SpectreUserInput>())
            .AddSingleton<ILifecycleEventHandler, SpectreLifecycleOutput>()
            .AddHostedService<ConsoleUserBridge>();

        return services;
    }
}
