namespace Assistant.App.Mail;

internal static class MailReplyBody
{
    public static string Compose(string newBody, MailMessage original)
    {
        return $"""
            {newBody.TrimEnd()}

            ----- Original Message -----
            From: {original.From}
            Time: {MailTimestamp.FormatUtc(original.Timestamp)}

            {original.Body.TrimEnd()}
            """;
    }
}
