using Microsoft.Extensions.AI;

namespace RagEval.Tests.Support;

/// <summary>A chat model that returns a fixed reply and records what it was asked.</summary>
internal sealed class ScriptedChatClient(Func<IReadOnlyList<ChatMessage>, string> reply) : IChatClient
{
    public ScriptedChatClient(string reply) : this(_ => reply)
    {
    }

    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Calls.Add(list);
        var text = reply(list);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { OutputTokenCount = text.Split(' ').Length },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
