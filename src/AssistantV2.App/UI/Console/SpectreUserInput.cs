using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

namespace AssistantV2.App.UI.Console;

public interface IUserInput
{
    /// <summary>Draw the startup banner before any lifecycle output.</summary>
    void ShowBanner();

    Task<string> ReadAsync(CancellationToken cancellationToken);
}

public sealed class SpectreUserInput(ConsoleGate gate) : IUserInput
{
    private bool _initialized;

    public void ShowBanner()
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
        AnsiConsole.Write(new FigletText("AssistantV2")
        {
            Justification = Justify.Center,
            Color = Color.Cyan1
        });
        _initialized = true;
    }
}
