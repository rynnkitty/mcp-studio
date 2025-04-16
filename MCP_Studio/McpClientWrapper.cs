using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using System.Diagnostics;

namespace MCP_Studio
{
    public class McpClientWrapper : IAsyncDisposable
    {
        public IMcpClient _client = null!;
        public string ServerName { get; private set; } = "";
        private Process? _serverProcess;
        private int retryCount = 3; // 재시도 횟수
        public async Task<bool> InitializeAsync(string command, string[] args, string serverName = "")
        {
            ServerName = serverName;
           
            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                try
                {
                    // 이전 서버 프로세스 정리
                    if (_serverProcess != null && !_serverProcess.HasExited)
                    {
                        try
                        {
                            _serverProcess.Kill();
                            _serverProcess.Dispose();
                        }
                        catch { /* 무시 */ }
                    }

                    //// 서버 환경 검증
                    //if (command.ToLower() == "npx")
                    //{
                    //    // npm이 설치되어 있는지 확인
                    //    try
                    //    {
                    //        var npmProcess = Process.Start(new ProcessStartInfo
                    //        {
                    //            FileName = "npm",
                    //            Arguments = "-v",
                    //            RedirectStandardOutput = true,
                    //            UseShellExecute = false,
                    //            CreateNoWindow = true
                    //        });

                    //        await npmProcess!.WaitForExitAsync();

                    //        if (npmProcess.ExitCode != 0)
                    //        {
                    //            throw new Exception("npm이 설치되어 있지 않습니다.");
                    //        }
                    //    }
                    //    catch
                    //    {
                    //        throw new Exception("npm이 설치되어 있지 않거나 PATH에 등록되어 있지 않습니다.");
                    //    }
                    //}

                    var transport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = serverName,
                        Command = command,
                        Arguments = args
                    });

                    _client = await McpClientFactory.CreateAsync(transport);

                    return true;
                }
                catch (Exception ex)
                {
                    // 타임아웃 또는 서버 종료 문제 발생
                    if (attempt == retryCount)
                    {
                        // 로그 기록 (실제 구현은 적절한 로깅 시스템 사용)
                        Debug.WriteLine($"서버 초기화 실패 (시도 {attempt}/{retryCount}): {ex.Message}");
                        throw new Exception($"MCP 서버 '{serverName}' 초기화 실패: {ex.Message}", ex);
                    }

                    // 잠시 대기 후 재시도
                    await Task.Delay(2000);
                    Debug.WriteLine($"서버 초기화 재시도 중... (시도 {attempt}/{retryCount})");
                }
            }
            throw new Exception($"MCP 서버 '{serverName}' 초기화에 모든 시도 실패");
        }
        public async Task<IList<McpClientTool>> ListToolsAsync()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("MCP client is not initialized.");
            }
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
