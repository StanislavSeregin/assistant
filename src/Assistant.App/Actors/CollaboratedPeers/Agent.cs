using Proto;
using System.Threading.Tasks;

namespace Assistant.App.Actors.CollaboratedPeers;

public static class Agent
{
    public class Actor : IActor
    {
        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                _ => Task.CompletedTask
            };
        }
    }
}
