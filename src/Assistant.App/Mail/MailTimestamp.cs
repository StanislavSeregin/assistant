using System;
using System.Globalization;

namespace Assistant.App.Mail;

internal static class MailTimestamp
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string FormatUtc(DateTime timestamp) =>
        timestamp.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
}
