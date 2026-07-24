using HstReceipts.Infrastructure.Learning;
using Xunit;

namespace HstReceipts.Tests;

public sealed class OpenAiUsageRecorderTests
{
    [Fact]
    public void ParseUsage_ReadsPromptCompletionTotal()
    {
        const string json = """
            {"id":"chatcmpl-x","usage":{"prompt_tokens":120,"completion_tokens":45,"total_tokens":165},"choices":[]}
            """;

        var (prompt, completion, total) = OpenAiUsageRecorder.ParseUsage(json);

        Assert.Equal(120, prompt);
        Assert.Equal(45, completion);
        Assert.Equal(165, total);
    }

    [Fact]
    public void ParseUsage_MissingUsage_ReturnsZeros()
    {
        var (prompt, completion, total) = OpenAiUsageRecorder.ParseUsage("""{"choices":[]}""");
        Assert.Equal(0, prompt);
        Assert.Equal(0, completion);
        Assert.Equal(0, total);
    }
}
