using Microsoft.Extensions.Hosting;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App
{
    public class AIHostedService(ISubject<KekMessage?> kekMessageSubject) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            kekMessageSubject.OnNext(new KekMessage("Hello"));
        }
    }
}
