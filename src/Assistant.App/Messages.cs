namespace Assistant.App
{
    public static class Messages
    {
        public record Ask(string Text);

        public record Rendered;

        public record ResponseFromSubAgent(string? Name, string Text);
    }
}
