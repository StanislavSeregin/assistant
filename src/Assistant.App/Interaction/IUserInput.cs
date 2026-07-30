using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Interaction;

public interface IUserInput
{
    Task<string> ReadAsync(CancellationToken cancellationToken);
}
