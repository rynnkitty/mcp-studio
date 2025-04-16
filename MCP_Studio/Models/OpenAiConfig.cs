namespace MCP_Studio.Models
{
    public class OpenAiConfig
    {
        public string ModelID { get; set; }
        public string ApiKey { get; set; }
        public string ResourceName { get; set; }
        public string DeploymentId { get; set; }
        public string ApiVersion { get; set; }
        public string Endpoint { get; set; } // Azure 엔드포인트 추가
    }
}
