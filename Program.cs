using Azure.AI.Inference;
using Azure.AI.Projects;
using Azure.Identity;
using NUnit.Framework;

namespace Azure.AI.Samples
{
    public partial class Program
    {
        public async static Task Main(string[] args)
        {
            await AzureAIBasic();
            await AzureAIStreaming();
            await ConnectionExample();
        }

        public static async Task AzureAIBasic()
        {
            Console.WriteLine($"-------- Azure AI Basic Sample --------");

            var connectionString = Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
            AgentsClient client = new AgentsClient(connectionString, new DefaultAzureCredential());

            // Step 1: Create an agent
            Response<Agent> agentResponse = await client.CreateAgentAsync(
                model: "gpt-4-1106-preview",
                name: "Math Tutor",
                instructions: "You are a personal math tutor. Write and run code to answer math questions.",
                tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() });
            Agent agent = agentResponse.Value;

            // Intermission: agent should now be listed

            Response<PageableList<Agent>> agentListResponse = await client.GetAgentsAsync();

            //// Step 2: Create a thread
            Response<AgentThread> threadResponse = await client.CreateThreadAsync();
            AgentThread thread = threadResponse.Value;

            // Step 3: Add a message to a thread
            Response<ThreadMessage> messageResponse = await client.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                "I need to solve the equation `3x + 11 = 14`. Can you help me?");
            ThreadMessage message = messageResponse.Value;

            // Intermission: message is now correlated with thread
            // Intermission: listing messages will retrieve the message just added

            Response<PageableList<ThreadMessage>> messagesListResponse = await client.GetMessagesAsync(thread.Id);
            Assert.That(messagesListResponse.Value.Data[0].Id == message.Id);

            // Step 4: Run the agent
            Response<ThreadRun> runResponse = await client.CreateRunAsync(
                thread.Id,
                agent.Id,
                additionalInstructions: "Please address the user as Jane Doe. The user has a premium account.");
            ThreadRun run = runResponse.Value;

            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                runResponse = await client.GetRunAsync(thread.Id, runResponse.Value.Id);
            }
            while (runResponse.Value.Status == RunStatus.Queued
                || runResponse.Value.Status == RunStatus.InProgress);

            Response<PageableList<ThreadMessage>> afterRunMessagesResponse
                = await client.GetMessagesAsync(thread.Id);
            IReadOnlyList<ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

            // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
            foreach (ThreadMessage threadMessage in messages)
            {
                Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
                foreach (MessageContent contentItem in threadMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        Console.Write(textItem.Text);
                    }
                    else if (contentItem is MessageImageFileContent imageFileItem)
                    {
                        Console.Write($"<image from ID: {imageFileItem.FileId}");
                    }
                    Console.WriteLine();
                }
            }
        }

        public static async Task AzureAIStreaming()
        {
            Console.WriteLine($"-------- Azure AI Streaming Sample--------");

            var connectionString = Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
            AgentsClient client = new AgentsClient(connectionString, new DefaultAzureCredential());

            Response<Agent> agentResponse = await client.CreateAgentAsync(
                model: "gpt-4-1106-preview",
                name: "My Friendly Test Assistant",
                instructions: "You politely help with math questions. Use the code interpreter tool when asked to visualize numbers.",
                tools: new List<ToolDefinition> { new CodeInterpreterToolDefinition() });
            Agent agent = agentResponse.Value;

            Response<AgentThread> threadResponse = await client.CreateThreadAsync();
            AgentThread thread = threadResponse.Value;

            Response<ThreadMessage> messageResponse = await client.CreateMessageAsync(
                thread.Id,
                MessageRole.User,
                "Hi, Assistant! Draw a graph for a line with a slope of 4 and y-intercept of 9.");
            ThreadMessage message = messageResponse.Value;

            await foreach (var streamingUpdate in client.CreateRunStreamingAsync(thread.Id, agent.Id))
            {
                Console.WriteLine(streamingUpdate.UpdateKind);
                if (streamingUpdate.UpdateKind == StreamingUpdateReason.RunCreated)
                {
                    Console.WriteLine($"--- Run started! ---");
                }
                else if (streamingUpdate is MessageContentUpdate contentUpdate)
                {
                    Console.Write(contentUpdate.Text);
                    if (contentUpdate.ImageFileId is not null)
                    {
                        Console.WriteLine($"[Image content file ID: {contentUpdate.ImageFileId}");
                    }
                }
            }
        }

        public static async Task ConnectionExample()
        {
            var connectionString = Environment.GetEnvironmentVariable("PROJECT_CONNECTION_STRING");
            var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME");

            AIProjectClient client = new AIProjectClient(connectionString, new DefaultAzureCredential());
            var connectionsClient = client.GetConnectionsClient();

            ConnectionResponse connection = connectionsClient.GetDefaultConnection(ConnectionType.Serverless, true);

            if (connection.Properties.AuthType == AuthenticationType.ApiKey)
            {
                var apiKeyAuthProperties = connection.Properties as ConnectionPropertiesApiKeyAuth;
                if (string.IsNullOrWhiteSpace(apiKeyAuthProperties.Target))
                {
                    throw new ArgumentException("The API key authentication target URI is missing or invalid.");
                }

                if (!Uri.TryCreate(apiKeyAuthProperties.Target, UriKind.Absolute, out var endpoint))
                {
                    throw new UriFormatException("Invalid URI format in API key authentication target.");
                }

                var credential = new AzureKeyCredential(apiKeyAuthProperties.Credentials.Key);
                ChatCompletionsClient chatClient = new ChatCompletionsClient(endpoint, credential);

                var requestOptions = new ChatCompletionsOptions()
                {
                    Messages =
                {
                    new ChatRequestSystemMessage("You are a helpful assistant."),
                    new ChatRequestUserMessage("How many feet are in a mile?"),
                },
                    Model = modelDeploymentName
                };

                Response<ChatCompletions> response = await chatClient.CompleteAsync(requestOptions);
                Console.WriteLine(response.Value.Content);
            }
        }
    }
}