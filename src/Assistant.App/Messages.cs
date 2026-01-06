using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Reactive.Subjects;

namespace Assistant.App;

public interface IMessage;

public record StreamingMessage(string Name, IAsyncEnumerable<string> LiveContent) : IMessage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessages(this IServiceCollection services)
    {
        var messageSubject = new BehaviorSubject<IMessage?>(default);
        return services
            .AddSingleton<ISubject<IMessage?>>(messageSubject)
            .AddSingleton<IObservable<IMessage?>>(messageSubject);
    }
}
