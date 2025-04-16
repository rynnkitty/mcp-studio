using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace MCP_Studio
{
    public class McpClientWrapper : IAsyncDisposable
    {
        public IMcpClient _client = null!;
        public async Task InitializeAsync(string command, string[] args)
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {               
                Name = "everything",
                Command = command,
                Arguments = args
            });

            _client = await McpClientFactory.CreateAsync(transport);
         
        }
        public async Task<IList<McpClientTool>> ListToolsAsync()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("MCP client is not initialized.");
            }
            // (IList<McpClientTool>)
            //IList<McpClientTool> ilist = (IList<McpClientTool>)await _client.ListToolsAsync();
            //return new List<McpClientTool>(ilist);
            return await _client.ListToolsAsync();

        }
        public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var result = await _client.CallToolAsync(toolName, parameters, default);
            return result.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;
        }
       
        public async ValueTask DisposeAsync()
        {
            if (_client != null)
            {
                await _client.DisposeAsync();
            }
        }
    }
}
