using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.UI;

public interface IUserInput
{
    Task<string> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// UI host lifecycle (banner / theme / setup) separate from input.
/// </summary>
public interface IAppUi
{
    void Initialize();
}
