using Assistant.App.Interaction;
using Spectre.Console;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Clients.Console;

public sealed class SpectreUserInput(ConsoleGate gate) : IUserInput
{
    private bool _initialized;

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.EnterAsync(cancellationToken);
        try
        {
            Initialize();
            AnsiConsole.WriteLine();
            return AnsiConsole.Prompt(new TextPrompt<string>(">").AllowEmpty());
        }
        finally
        {
            gate.Exit();
        }
    }

    private void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        System.Console.OutputEncoding = Encoding.UTF8;
        System.Console.InputEncoding = Encoding.UTF8;
        System.Console.Clear();
        AnsiConsole.Write(new FigletText("Assistant")
        {
            Justification = Justify.Center,
            Color = Color.Cyan
        });
        _initialized = true;
    }
}
