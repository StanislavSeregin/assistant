using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;

namespace Assistant.App;

public interface IAIEvent;

public record StreamingAIResponse(string Name, IAsyncEnumerable<string> LiveContent) : IAIEvent;

public interface IUIEvent;

public record HumanMessage(string Text) : IUIEvent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessages(this IServiceCollection services)
    {
        var messageSubject = new BehaviorSubject<IAIEvent?>(default);
        var humanRequestSubject = new Subject<IUIEvent>();
        return services
            .AddSingleton<ISubject<IAIEvent?>>(messageSubject)
            .AddSingleton<IObservable<IAIEvent?>>(messageSubject)
            .AddSingleton<ISubject<IUIEvent>>(humanRequestSubject)
            .AddSingleton<IObservable<IUIEvent>>(humanRequestSubject);
    }
}
