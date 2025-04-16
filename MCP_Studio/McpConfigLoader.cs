using System.IO;
using Newtonsoft.Json.Linq;

namespace MCP_Studio
{
    public class McpServerConfig
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string[] Args { get; set; } = Array.Empty<string>();
    }
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
        public static List<McpServerConfig> LoadAllMcpServers(string configPath)
        {
            var result = new List<McpServerConfig>();
            var json = File.ReadAllText(configPath);
            var jObject = JObject.Parse(json);
            var servers = jObject["mcpServers"];

            if (servers == null)
                return result;

            foreach (var server in servers.Children<JProperty>())
            {
                var serverName = server.Name;
                var serverConfig = server.Value;

                result.Add(new McpServerConfig
                {
                    Name = serverName,
                    Command = serverConfig["command"]?.ToString() ?? "",
                    Args = serverConfig["args"]?.ToObject<string[]>() ?? Array.Empty<string>()
                });
            }

            return result;
        }
    }
}
