using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.UI;

/// <summary>
/// UI host lifecycle. <see cref="Run"/> owns the interactive session (blocks until quit).
/// </summary>
public interface IAppUi
{
    void Initialize();

    void Run();
}
