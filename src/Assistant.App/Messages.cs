using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reactive.Subjects;

namespace Assistant.App
{
    public record KekMessage(string Text);

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMessages(this IServiceCollection services)
        {
            var KekMessageSubject = new BehaviorSubject<KekMessage?>(default);
            return services
                .AddSingleton<ISubject<KekMessage?>>(KekMessageSubject)
                .AddSingleton<IObservable<KekMessage?>>(KekMessageSubject);
        }
    }
}
