using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AssistantV2.App.UI.Console;

/// <summary>
/// Windows consoles often start on OEM CP437/850, which replaces Cyrillic with '?'.
/// Force UTF-8 for both .NET encodings and the Win32 console code pages.
/// </summary>
public static class ConsoleUtf8
{
    private const uint Utf8CodePage = 65001;

    public static void Enable()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        System.Console.OutputEncoding = utf8;
        System.Console.InputEncoding = utf8;

        if (OperatingSystem.IsWindows())
        {
            SetConsoleOutputCP(Utf8CodePage);
            SetConsoleCP(Utf8CodePage);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCP(uint wCodePageID);
}
