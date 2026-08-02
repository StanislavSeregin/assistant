using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.UI.Console;

public sealed class SpectreUserInput(ConsoleGate gate) : IUserInput, IAppUi
{
    private bool _initialized;

    public void Initialize()
    {
        gate.Enter();
        try
        {
            EnsureBanner();
        }
        finally
        {
            gate.Exit();
        }
    }

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.EnterAsync(cancellationToken);
        try
        {
            EnsureBanner();
            AnsiConsole.WriteLine();
            return AnsiConsole.Prompt(new TextPrompt<string>(">").AllowEmpty());
        }
        finally
        {
            gate.Exit();
        }
    }

    private void EnsureBanner()
    {
        if (_initialized)
        {
            return;
        }

        System.Console.Clear();
        AnsiConsole.Write(new FigletText("Assistant")
        {
            Justification = Justify.Center,
            Color = Color.Cyan1
        });
        _initialized = true;
    }
}
