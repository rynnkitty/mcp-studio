using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

using MCP_Studio.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

using OpenAI.Chat;
using System.Text;
using ModelContextProtocol.Protocol.Types;

namespace MCP_Studio
{
    public partial class Form1 : Form
    {
        //OpenAI
        private ChatClient _chatClient;
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private string _modelId;
        private OpenAiConfig aiConfig;
        //MCP
        private List<McpClientWrapper> _mcpClients = new List<McpClientWrapper>();
        private McpClientWrapper _mcpClient;
        private IList<McpClientTool> _tools;
        private List<ChatTool> _openAiTools;
        private string _configPath = "mcp_config.json";
        //
        private bool _isProcessingRequest = false;
        private readonly object _lockObject = new object();

        public Form1()
        {
            InitializeComponent();
            InitializeOpenAI();
        }

        private void InitializeOpenAI()
        {
            try
            {
                // JSON 파일에서 OpenAI API 설정을 읽어옵니다.
                aiConfig = LoadOpenAiConfig("config.json");
                _modelId = aiConfig.ModelID;

                _chatClient = new ChatClient(aiConfig.ModelID, aiConfig.ApiKey);
                AppendToChat("System", "OpenAI 클라이언트 초기화 완료");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("설정 파일을 찾을 수 없습니다.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("설정 파일 형식이 잘못되었습니다.", ex);
            }
            catch (Exception ex)
            {
                HandleError("OpenAI 초기화 중 오류가 발생했습니다.", ex);
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await InitializeMcpClientAsync();
        }

        private async Task InitializeMcpClientAsync()
        {
            try
            {
                // 기존 mcpClient 정리
                if (_mcpClient != null)
                {
                    await _mcpClient.DisposeAsync();
                    _mcpClient = null;
                }

                // 기존 연결된 모든 클라이언트 해제
                foreach (var client in _mcpClients)
                {
                    await client.DisposeAsync();
                }
                _mcpClients.Clear();

                SetStatus("MCP 서버 초기화 중...");
                richTextBox1.Enabled = false;

                // 기존 클라이언트 정리
                await CleanupMcpClientsAsync();
                // 모든 서버 정보 로드
                var servers = McpConfigLoader.LoadAllMcpServers(_configPath);
                if (servers.Count == 0)
                {
                    toolStripStatusLabel1.Text = "구성된 MCP 서버가 없습니다.";
                    return;
                }
                // 상태바 업데이트
                toolStripStatusLabel1.Text = $"MCP 서버 {servers.Count}개를 불러오는 중...";
                // 리스트박스 초기화
                listBox1.Items.Clear();

                // 각 서버에 대해 클라이언트 생성
                foreach (var server in servers)
                {
                    var mcpClient = new McpClientWrapper();
                    try
                    {
                        bool success = await mcpClient.InitializeAsync(server.Command, server.Args, server.Name);
                        _mcpClients.Add(mcpClient);
                        if (success)
                        {
                            // 리스트박스에 서버 이름 추가
                            listBox1.Items.Add(server.Name);
                            if (mcpClient != null)
                            {
                                // 서버 도구 목록 표시
                                await DisplayServerToolsAsync(mcpClient);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await mcpClient.DisposeAsync();
                        toolStripStatusLabel1.Text = $"MCP 서버 '{server.Name}' 연결 실패: {ex.Message}";
                    }
                }

                //_mcpClient = new McpClientWrapper();
                //var (command, args) = McpConfigLoader.LoadMcpServerConfig("mcp_config.json", "everything");

                //await _mcpClient.InitializeAsync(command, args);

                AppendToChat("System", "MCP 클라이언트 초기화 완료");




                richTextBox1.Enabled = true;
                SetStatus("준비됨");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("MCP 설정 파일을 찾을 수 없습니다.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("MCP 설정 파일 형식이 잘못되었습니다.", ex);
            }
            catch (Exception ex)
            {
                HandleError("MCP 클라이언트 초기화 실패", ex);
            }
        }
        private async Task CleanupMcpClientsAsync()
        {
            // 기존 mcpClient 정리
            if (_mcpClient != null)
            {
                await _mcpClient.DisposeAsync();
                _mcpClient = null;
            }

            // 기존 연결된 모든 클라이언트 해제
            foreach (var client in _mcpClients)
            {
                await client.DisposeAsync();
            }
            _mcpClients.Clear();
        }
        private async Task DisplayServerToolsAsync(McpClientWrapper client)
        {
            try
            {
                // 사용 가능한 도구 목록 가져오기
                var tools = await client.ListToolsAsync();

                AppendToChat("System", $"{tools.Count}개의 도구를 찾았습니다.");

                // 도구 목록이 성공적으로 가져와진 후에만 변환 작업 수행
                if (tools != null && tools.Count > 0)
                {
                    // 사용 가능한 도구를 OpenAI 형식으로 변환
                    _openAiTools = ConvertMcpToolsToOpenAiTools(tools);
                    AppendToChat("System", $"{_openAiTools.Count}개의 도구를 OpenAI 형식으로 변환했습니다.");

                    // 도구 이름과 설명 로깅
                    foreach (var tool in _openAiTools)
                    {
                        AppendToChat("System", $"도구: {tool.FunctionName}");
                    }
                }
                else
                {
                    AppendToChat("System", "사용 가능한 도구가 없습니다.");
                    toolStripStatusLabel1.Text = "도구 없음";
                }

               
            }
            catch (Exception ex)
            {
                richTextBox3.Text = $"도구 목록을 가져오는 중 오류 발생: {ex.Message}";
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (_isProcessingRequest)
            {
                MessageBox.Show("이전 요청이 처리 중입니다. 잠시 기다려주세요.", "처리 중", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _isProcessingRequest = true;
                SetStatus("처리 중...");
                await HandleChatAsync();
            }
            catch (Exception ex)
            {
                HandleError("채팅 처리 중 오류가 발생했습니다.", ex);
            }
            finally
            {
                _isProcessingRequest = false;
                SetStatus("준비됨");
            }
        }

        private void AppendToChat(string sender, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendToChat(sender, message)));
                return;
            }

            richTextBox2.AppendText($"{sender}: {message}\n\n");
            richTextBox2.ScrollToCaret();
        }

        private void SetStatus(string status)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => SetStatus(status)));
                return;
            }
            toolStripStatusLabel1.Text = status;
        }

        private async Task HandleChatAsync()
        {
            // 입력 유효성 검사
            var userInput = richTextBox1.Text.Trim();
            if (string.IsNullOrEmpty(userInput))
            {
                MessageBox.Show("메시지를 입력해주세요.", "입력 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_chatClient == null)
            {
                HandleError("OpenAI 클라이언트가 초기화되지 않았습니다.", null);
                return;
            }

            AppendToChat("You", userInput);
            richTextBox1.Clear();

            _chatHistory.Add(new UserChatMessage(userInput));

            try
            {
                var options = CreateChatCompletionOptions();

                // 초기 AI 응답 요청
                var response = await _chatClient.CompleteChatAsync(_chatHistory, options);

                // 도구 호출이 있는 경우
                if (response.Value.ToolCalls != null && response.Value.ToolCalls.Count > 0)
                {
                    await ProcessToolCallsAsync(response.Value.ToolCalls, options);
                }
                // 일반 응답인 경우
                else if (response.Value.Content != null && response.Value.Content.Count > 0)
                {
                    ProcessAssistantResponse(response);
                }
                else
                {
                    AppendToChat("System", "GPT 응답이 없습니다.");
                }
            }
            catch (Exception ex)
            {
                _chatHistory.Add(new AssistantChatMessage("죄송합니다. 응답을 처리하는 도중 오류가 발생했습니다."));
                HandleError("AI 응답 요청 중 오류가 발생했습니다.", ex);
            }
        }

        private ChatCompletionOptions CreateChatCompletionOptions()
        {
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 1000,
                Temperature = 0.7f
            };

            if (_openAiTools != null && _openAiTools.Count > 0)
            {
                foreach (var tool in _openAiTools)
                {
                    options.Tools.Add(tool);
                }
            }

            return options;
        }

        private async Task ProcessToolCallsAsync(IReadOnlyList<object> toolCalls, ChatCompletionOptions options)
        {
            AppendToChat("System", $"{toolCalls.Count}개의 도구 호출을 처리합니다...");

            // 도구 응답을 저장할 변수
            Dictionary<string, string> toolResponses = new Dictionary<string, string>();

            // 마지막 사용자 메시지 찾기
            string lastUserMessage = "";
            for (int i = _chatHistory.Count - 1; i >= 0; i--)
            {
                if (_chatHistory[i] is UserChatMessage userMessage)
                {
                    // 사용자 메시지의 내용 가져오기 (SDK에 따라 속성명이 다를 수 있음)
                    lastUserMessage = userMessage.Content.ToString();
                    break;
                }
            }

            // 각 tool 호출에 대해 도구 실행
            foreach (var toolCall in toolCalls)
            {
                dynamic dynamicToolCall = toolCall;
                try
                {
                    string functionName = dynamicToolCall.FunctionName;
                    BinaryData argumentsBinaryData = dynamicToolCall.FunctionArguments;
                    string toolCallId = dynamicToolCall.Id;

                    string argumentsJson = argumentsBinaryData.ToString();
                    AppendToChat("System", $"도구 호출: {functionName}");
                    AppendToChat("System", $"인자: {argumentsJson}");

                    var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(argumentsJson);

                    if (_mcpClient == null)
                    {
                        AppendToChat("System", "MCP 클라이언트가 초기화되지 않았습니다.");
                        continue;
                    }

                    string mcpResponse = await _mcpClient.CallToolAsync(functionName, parameters);
                    AppendToChat("System", $"도구 응답: {mcpResponse}");

                    // 도구 응답 저장
                    toolResponses[functionName] = mcpResponse;
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"도구 실행 실패: {ex.Message}");
                }
            }

            try
            {
                // 완전히 새로운 대화 시작
                var newConversation = new List<ChatMessage>();

                // 초기 시스템 메시지 추가 (선택사항)
                newConversation.Add(new SystemChatMessage("이전에 실행된 도구의 결과를 설명해드립니다."));

                // 원래 사용자 요청과 도구 결과를 하나의 메시지로 결합
                StringBuilder combinedMessage = new StringBuilder();
                combinedMessage.AppendLine($"원래 요청: {lastUserMessage}");
                combinedMessage.AppendLine();
                combinedMessage.AppendLine("도구 실행 결과:");

                foreach (var response in toolResponses)
                {
                    combinedMessage.AppendLine($"- {response.Key}: {response.Value}");
                }

                combinedMessage.AppendLine();
                combinedMessage.AppendLine("이 결과에 기반해 응답해주세요.");

                newConversation.Add(new UserChatMessage(combinedMessage.ToString()));

                AppendToChat("System", "새로운 대화로 응답을 요청합니다.");

                // 사용 중인 모델 로깅 (디버깅용)
                AppendToChat("System", $"사용 중인 모델: {aiConfig.ModelID}");

                // 새 대화로 API 호출
                var finalResponse = await _chatClient.CompleteChatAsync(newConversation, options);

                AppendToChat("System", $"응답 상태: {finalResponse.Value != null}");

                if (finalResponse.Value != null &&
                    finalResponse.Value.Content != null &&
                    finalResponse.Value.Content.Count > 0)
                {
                    var assistantReply = finalResponse.Value.Content[0].Text?.Trim();
                    if (!string.IsNullOrEmpty(assistantReply))
                    {
                        AppendToChat("ChatGPT", assistantReply);

                        // 원래 채팅 이력에 도구 응답 및 어시스턴트 응답 추가
                        // 도구 실행 결과를 포함한 사용자 메시지
                        string toolResultsForHistory = "도구 실행 결과:\n";
                        foreach (var response in toolResponses)
                        {
                            toolResultsForHistory += $"- {response.Key}: {response.Value}\n";
                        }

                        _chatHistory.Add(new UserChatMessage(toolResultsForHistory));
                        _chatHistory.Add(new AssistantChatMessage(assistantReply));
                    }
                    else
                    {
                        AppendToChat("System", "GPT 응답이 비어 있습니다.");
                        // 대체 응답 추가
                        string fallbackReply = "도구 실행 결과를 확인했습니다.";
                        AppendToChat("ChatGPT", fallbackReply);
                        _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                    }
                }
                else
                {
                    AppendToChat("System", "도구 호출 후 GPT 응답이 없습니다.");
                    // 대체 응답 추가
                    string fallbackReply = "도구가 실행되었습니다.";
                    AppendToChat("ChatGPT", fallbackReply);
                    _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                }
            }
            catch (Exception ex)
            {
                AppendToChat("System", $"최종 응답 요청 실패: {ex.Message}");
                AppendToChat("System", $"스택 트레이스: {ex.StackTrace}");
                HandleError("도구 호출 후 최종 응답 요청 실패", ex);

                // 오류 발생시 대체 응답 추가
                string errorReply = "도구 실행 후 응답을 받는데 문제가 발생했습니다.";
                AppendToChat("ChatGPT", errorReply);
                _chatHistory.Add(new AssistantChatMessage(errorReply));
            }
        }

        private void ProcessAssistantResponse(System.ClientModel.ClientResult<ChatCompletion> response)
        {
            if (response.Value.Content != null && response.Value.Content.Count > 0)
            {
                var assistantReply = response.Value.Content[0].Text.Trim();
                AppendToChat("ChatGPT", assistantReply);

                // 채팅 이력에 AI 응답 추가
                _chatHistory.Add(new AssistantChatMessage(assistantReply));
            }
            else
            {
                AppendToChat("System", "GPT 응답이 없습니다.");
            }
        }

        private void HandleError(string message, Exception ex)
        {
            string errorDetails = ex != null ? $"\n\n상세 오류: {ex.Message}" : "";
            AppendToChat("Error", message + errorDetails);

            // 중요 오류는 메시지 박스로도 표시
            if (ex != null && !(ex is FileNotFoundException || ex is JsonException))
            {
                MessageBox.Show(message + errorDetails, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // 로그 파일에 오류 기록
            LogError(message, ex);
        }

        private void LogError(string message, Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(logFolder, $"error_{DateTime.Now:yyyyMMdd}.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

                if (ex != null)
                {
                    logEntry += $"\nException: {ex.GetType().Name}\nMessage: {ex.Message}\nStackTrace: {ex.StackTrace}";
                }

                File.AppendAllText(logFile, logEntry + "\n\n");
            }
            catch
            {
                // 로깅 중 오류 발생 시 무시 (로깅 실패가 애플리케이션 실행에 영향을 주지 않도록)
            }
        }

        private async void richTextBox1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (_isProcessingRequest)
                {
                    MessageBox.Show("이전 요청이 처리 중입니다. 잠시 기다려주세요.", "처리 중", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    _isProcessingRequest = true;
                    SetStatus("처리 중...");
                    await HandleChatAsync();
                }
                catch (Exception ex)
                {
                    HandleError("채팅 처리 중 오류가 발생했습니다.", ex);
                }
                finally
                {
                    _isProcessingRequest = false;
                    SetStatus("준비됨");
                }
            }
        }

        private OpenAiConfig LoadOpenAiConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"설정 파일을 찾을 수 없습니다: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<OpenAiConfig>(json);

            if (string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ModelID))
            {
                throw new InvalidOperationException("설정 파일에 필수 정보(ApiKey 또는 ModelID)가 누락되었습니다.");
            }

            return config;
        }

        // MCP 도구를 OpenAI 형식으로 변환하는 함수 추가
        private List<ChatTool> ConvertMcpToolsToOpenAiTools(IList<McpClientTool> mcpTools)
        {
            var openAiTools = new List<ChatTool>();

            if (mcpTools == null || mcpTools.Count == 0)
            {
                AppendToChat("System", "사용 가능한 도구가 없습니다.");
                return null;
            }
            // MCP 도구를 OpenAI 툴로 변환합니다.
            openAiTools = mcpTools.Select(tool =>
            {
                try
                {
                    // MCP 도구의 입력 스키마를 JSON 문자열로 변환합니다.
                    string rawJsonSchema = tool.ProtocolTool.InputSchema.GetRawText();

                    // JSON 문자열을 BinaryData로 변환합니다.
                    var parameters = BinaryData.FromString(rawJsonSchema);

                    // OpenAI 툴을 생성합니다.
                    return ChatTool.CreateFunctionTool(
                        functionName: tool.Name,
                        functionDescription: tool.Description,
                        functionParameters: parameters
                    );
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"도구 {tool.Name} 변환 실패: {ex.Message}");
                    return null;
                }
            })
            .Where(tool => tool != null)
            .ToList();

            return openAiTools;
        }
        private async void button2_Click(object sender, EventArgs e)
        {
            if (_mcpClient == null)
            {
                HandleError("MCP 클라이언트가 초기화되지 않았습니다.", null);
                return;
            }

            try
            {
                SetStatus("도구 목록 불러오는 중...");
                _tools = await _mcpClient.ListToolsAsync();

                if (_tools == null || _tools.Count == 0)
                {
                    AppendToChat("System", "사용 가능한 도구가 없습니다.");
                    return;
                }

                AppendToChat("System", $"{_tools.Count}개의 도구를 불러왔습니다:");

                foreach (var tool in _tools)
                {
                    AppendToChat("System", $"도구: {tool.Name}");
                    AppendToChat("System", $"설명: {tool.Description}");
                    AppendToChat("System", $"스키마: {tool.ProtocolTool.InputSchema}");
                }

                // MCP 도구를 OpenAI 툴로 변환합니다.
                _openAiTools = _tools.Select(tool =>
                {
                    try
                    {
                        // MCP 도구의 입력 스키마를 JSON 문자열로 변환합니다.
                        string rawJsonSchema = tool.ProtocolTool.InputSchema.GetRawText();

                        // JSON 문자열을 BinaryData로 변환합니다.
                        var parameters = BinaryData.FromString(rawJsonSchema);

                        // OpenAI 툴을 생성합니다.
                        return ChatTool.CreateFunctionTool(
                            functionName: tool.Name,
                            functionDescription: tool.Description,
                            functionParameters: parameters
                        );
                    }
                    catch (Exception ex)
                    {
                        AppendToChat("System", $"도구 {tool.Name} 변환 실패: {ex.Message}");
                        return null;
                    }
                })
                .Where(tool => tool != null)
                .ToList();

                AppendToChat("System", $"{_openAiTools.Count}개의 도구가 OpenAI 형식으로 변환되었습니다.");
                SetStatus("준비됨");
            }
            catch (Exception ex)
            {
                HandleError("도구 목록을 불러오는 중 오류가 발생했습니다.", ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 리소스 정리
            try
            {
                // _mcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("MCP 클라이언트 종료 중 오류가 발생했습니다.", ex);
            }

            base.OnFormClosing(e);
        }

        private async void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox1.SelectedIndex >= 0 && listBox1.SelectedIndex < _mcpClients.Count)
            {
                McpClientWrapper mcpClient = _mcpClients[listBox1.SelectedIndex];

                // 사용 가능한 도구 목록 가져오기
                var tools = await mcpClient.ListToolsAsync();

                richTextBox3.Clear();

                richTextBox3.AppendText($"서버: {mcpClient.ServerName}\n");
                richTextBox3.AppendText($"도구 수: {tools.Count}\n\n");

                foreach (var tool in tools)
                {
                    richTextBox3.AppendText($"도구 이름: {tool.Name}\n");
                    richTextBox3.AppendText($"설명: {tool.Description}\n");
                    richTextBox3.AppendText("-----------------------------------\n");
                }

            }
        }
    }
}