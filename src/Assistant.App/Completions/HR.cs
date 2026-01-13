using OpenAI.Chat;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Completions;

public static class HR
{
    public static async Task<string> GetResume(ChatClient client, string position, string jobDescription, CancellationToken cancellationToken)
    {
        var response = await client.CompleteChatAsync(
            messages: [
                new SystemChatMessage("You are an expert resume writer. Create a perfect, tailored resume based on the job description."),
                new UserChatMessage($"""
                Create a perfect resume for this job description. 

                RULES:
                1. Generate the resume in the SAME LANGUAGE as the job description
                2. Structure: Contact Info, Professional Summary, Work Experience, Skills, Education
                3. Match keywords from the job description
                4. Quantify achievements with numbers
                5. Output in clean markdown, no explanations

                Position:
                {position}

                Job Description:
                {jobDescription}
                """)
            ],
            options: new ChatCompletionOptions
            {
                Temperature = 0.3f,
                MaxOutputTokenCount = 2000
            },
            cancellationToken: cancellationToken);

        return response.Value.Content.FirstOrDefault()?.Text ?? string.Empty;
    }
}
