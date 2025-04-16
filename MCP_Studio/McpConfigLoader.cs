using System.IO;
using Newtonsoft.Json.Linq;

namespace MCP_Studio
{
    public class McpConfigLoader
    {
        public static (string command, string[] args) LoadMcpServerConfig(string configPath, string serverKey)
        {
            var json = File.ReadAllText(configPath);
            var jObject = JObject.Parse(json);
            var serverConfig = jObject["mcpServers"]?[serverKey];

            if (serverConfig == null)
                throw new Exception($"'{serverKey}'에 대한 MCP 서버 설정을 찾을 수 없습니다.");

            var command = serverConfig["command"]?.ToString();
            var args = serverConfig["args"]?.ToObject<string[]>();

            return (command, args);
        }
    }
}
