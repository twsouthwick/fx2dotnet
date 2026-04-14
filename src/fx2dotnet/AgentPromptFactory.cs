using Microsoft.Agents.AI;
using Microsoft.Agents.ObjectModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using System.Text.Json;

namespace fx2dotnet
{
    internal static class AgentPromptExtensions
    {
        public static  AIAgent CreateAgent(this AIAgent agent, IFileInfo file)
        {
            if (!file.Exists)
            {
                throw new FileNotFoundException("Could not find prompt", file.Name);
            }

            return new FileAIAgent(agent, file);
        }

        private class FileAIAgent : AIAgent
        {
            private readonly AIAgent _other;
            private readonly ChatMessage _instructions;

            public FileAIAgent(AIAgent other, IFileInfo file)
            {
                _other = other;

                using var stream = file.CreateReadStream();
                using var reader = new StreamReader(stream);

                var prompt = AgentPromptParser.Parse(reader.ReadToEnd());
                _instructions = new ChatMessage(ChatRole.System, prompt.Body);
            }

            protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            {
                var s = await _other.CreateSessionAsync(cancellationToken);
                return s;
            }

            protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            {
                return _other.DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);
            }

            protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            {
                return _other.RunAsync([_instructions, .. messages], session, options, cancellationToken);
            }

            protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
            {
                return _other.RunStreamingAsync([_instructions, .. messages], session, options, cancellationToken);
            }

            protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
            {
                return _other.SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);
            }
        }
    }
}
