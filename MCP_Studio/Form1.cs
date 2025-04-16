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

namespace MCP_Studio
{
    public partial class Form1 : Form
    {
        private ChatClient _chatClient;
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private string _modelId;
        private McpClientWrapper _mcpClient;
        private IList<McpClientTool> _tools;
        private List<ChatTool> _openAiTools;
        private bool _isProcessingRequest = false;
        private readonly object _lockObject = new object();
        private OpenAiConfig aiConfig;
        // Ŭ���� ��ܿ� �ʵ� �߰�
        // private List<ChatCompletionTool> _availableTools = new List<ChatCompletionTool>();
        public Form1()
        {
            InitializeComponent();
            InitializeOpenAI();
        }

        private void InitializeOpenAI()
        {
            try
            {
                // JSON ���Ͽ��� OpenAI API ������ �о�ɴϴ�.
                aiConfig = LoadOpenAiConfig("config.json");
                _modelId = aiConfig.ModelID;

                _chatClient = new ChatClient(aiConfig.ModelID, aiConfig.ApiKey);
                AppendToChat("System", "OpenAI Ŭ���̾�Ʈ �ʱ�ȭ �Ϸ�");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("���� ������ ã�� �� �����ϴ�.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("���� ���� ������ �߸��Ǿ����ϴ�.", ex);
            }
            catch (Exception ex)
            {
                HandleError("OpenAI �ʱ�ȭ �� ������ �߻��߽��ϴ�.", ex);
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
                SetStatus("MCP ���� �ʱ�ȭ ��...");
                richTextBox1.Enabled = false;
                _mcpClient = new McpClientWrapper();
                var (command, args) = McpConfigLoader.LoadMcpServerConfig("mcp_config.json", "everything");

                await _mcpClient.InitializeAsync(command, args);

                AppendToChat("System", "MCP Ŭ���̾�Ʈ �ʱ�ȭ �Ϸ�");

                // ��� ������ ���� ��� ��������
                var tools = await _mcpClient.ListToolsAsync();
                AppendToChat("System", $"{tools.Count}���� ������ ã�ҽ��ϴ�.");

                // ���� ����� ���������� �������� �Ŀ��� ��ȯ �۾� ����
                if (tools != null && tools.Count > 0)
                {
                    // ��� ������ ������ OpenAI �������� ��ȯ
                    _openAiTools = ConvertMcpToolsToOpenAiTools(tools);
                    AppendToChat("System", $"{_openAiTools.Count}���� ������ OpenAI �������� ��ȯ�߽��ϴ�.");

                    // ���� �̸��� ���� �α�
                    foreach (var tool in _openAiTools)
                    {
                        AppendToChat("System", $"����: {tool.FunctionName}");
                    }
                }
                else
                {
                    AppendToChat("System", "��� ������ ������ �����ϴ�.");
                    toolStripStatusLabel1.Text = "���� ����";
                }
                richTextBox1.Enabled = true;
                SetStatus("�غ��");
            }
            catch (FileNotFoundException ex)
            {
                HandleError("MCP ���� ������ ã�� �� �����ϴ�.", ex);
            }
            catch (JsonException ex)
            {
                HandleError("MCP ���� ���� ������ �߸��Ǿ����ϴ�.", ex);
            }
            catch (Exception ex)
            {
                HandleError("MCP Ŭ���̾�Ʈ �ʱ�ȭ ����", ex);
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            if (_isProcessingRequest)
            {
                MessageBox.Show("���� ��û�� ó�� ���Դϴ�. ��� ��ٷ��ּ���.", "ó�� ��", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _isProcessingRequest = true;
                SetStatus("ó�� ��...");
                await HandleChatAsync();
            }
            catch (Exception ex)
            {
                HandleError("ä�� ó�� �� ������ �߻��߽��ϴ�.", ex);
            }
            finally
            {
                _isProcessingRequest = false;
                SetStatus("�غ��");
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
            // �Է� ��ȿ�� �˻�
            var userInput = richTextBox1.Text.Trim();
            if (string.IsNullOrEmpty(userInput))
            {
                MessageBox.Show("�޽����� �Է����ּ���.", "�Է� ����", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_chatClient == null)
            {
                HandleError("OpenAI Ŭ���̾�Ʈ�� �ʱ�ȭ���� �ʾҽ��ϴ�.", null);
                return;
            }

            AppendToChat("You", userInput);
            richTextBox1.Clear();

            _chatHistory.Add(new UserChatMessage(userInput));

            try
            {
                var options = CreateChatCompletionOptions();

                // �ʱ� AI ���� ��û
                var response = await _chatClient.CompleteChatAsync(_chatHistory, options);

                // ���� ȣ���� �ִ� ���
                if (response.Value.ToolCalls != null && response.Value.ToolCalls.Count > 0)
                {
                    await ProcessToolCallsAsync(response.Value.ToolCalls, options);
                }
                // �Ϲ� ������ ���
                else if (response.Value.Content != null && response.Value.Content.Count > 0)
                {
                    ProcessAssistantResponse(response);
                }
                else
                {
                    AppendToChat("System", "GPT ������ �����ϴ�.");
                }
            }
            catch (Exception ex)
            {
                _chatHistory.Add(new AssistantChatMessage("�˼��մϴ�. ������ ó���ϴ� ���� ������ �߻��߽��ϴ�."));
                HandleError("AI ���� ��û �� ������ �߻��߽��ϴ�.", ex);
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
            AppendToChat("System", $"{toolCalls.Count}���� ���� ȣ���� ó���մϴ�...");

            // ���� ������ ������ ����
            Dictionary<string, string> toolResponses = new Dictionary<string, string>();

            // ������ ����� �޽��� ã��
            string lastUserMessage = "";
            for (int i = _chatHistory.Count - 1; i >= 0; i--)
            {
                if (_chatHistory[i] is UserChatMessage userMessage)
                {
                    // ����� �޽����� ���� �������� (SDK�� ���� �Ӽ����� �ٸ� �� ����)
                    lastUserMessage = userMessage.Content.ToString();
                    break;
                }
            }

            // �� tool ȣ�⿡ ���� ���� ����
            foreach (var toolCall in toolCalls)
            {
                dynamic dynamicToolCall = toolCall;
                try
                {
                    string functionName = dynamicToolCall.FunctionName;
                    BinaryData argumentsBinaryData = dynamicToolCall.FunctionArguments;
                    string toolCallId = dynamicToolCall.Id;

                    string argumentsJson = argumentsBinaryData.ToString();
                    AppendToChat("System", $"���� ȣ��: {functionName}");
                    AppendToChat("System", $"����: {argumentsJson}");

                    var parameters = JsonConvert.DeserializeObject<Dictionary<string, object>>(argumentsJson);

                    if (_mcpClient == null)
                    {
                        AppendToChat("System", "MCP Ŭ���̾�Ʈ�� �ʱ�ȭ���� �ʾҽ��ϴ�.");
                        continue;
                    }

                    string mcpResponse = await _mcpClient.CallToolAsync(functionName, parameters);
                    AppendToChat("System", $"���� ����: {mcpResponse}");

                    // ���� ���� ����
                    toolResponses[functionName] = mcpResponse;
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"���� ���� ����: {ex.Message}");
                }
            }

            try
            {
                // ������ ���ο� ��ȭ ����
                var newConversation = new List<ChatMessage>();

                // �ʱ� �ý��� �޽��� �߰� (���û���)
                newConversation.Add(new SystemChatMessage("������ ����� ������ ����� �����ص帳�ϴ�."));

                // ���� ����� ��û�� ���� ����� �ϳ��� �޽����� ����
                StringBuilder combinedMessage = new StringBuilder();
                combinedMessage.AppendLine($"���� ��û: {lastUserMessage}");
                combinedMessage.AppendLine();
                combinedMessage.AppendLine("���� ���� ���:");

                foreach (var response in toolResponses)
                {
                    combinedMessage.AppendLine($"- {response.Key}: {response.Value}");
                }

                combinedMessage.AppendLine();
                combinedMessage.AppendLine("�� ����� ����� �������ּ���.");

                newConversation.Add(new UserChatMessage(combinedMessage.ToString()));

                AppendToChat("System", "���ο� ��ȭ�� ������ ��û�մϴ�.");

                // ��� ���� �� �α� (������)
                AppendToChat("System", $"��� ���� ��: {aiConfig.ModelID}");

                // �� ��ȭ�� API ȣ��
                var finalResponse = await _chatClient.CompleteChatAsync(newConversation, options);

                AppendToChat("System", $"���� ����: {finalResponse.Value != null}");

                if (finalResponse.Value != null &&
                    finalResponse.Value.Content != null &&
                    finalResponse.Value.Content.Count > 0)
                {
                    var assistantReply = finalResponse.Value.Content[0].Text?.Trim();
                    if (!string.IsNullOrEmpty(assistantReply))
                    {
                        AppendToChat("ChatGPT", assistantReply);

                        // ���� ä�� �̷¿� ���� ���� �� ��ý���Ʈ ���� �߰�
                        // ���� ���� ����� ������ ����� �޽���
                        string toolResultsForHistory = "���� ���� ���:\n";
                        foreach (var response in toolResponses)
                        {
                            toolResultsForHistory += $"- {response.Key}: {response.Value}\n";
                        }

                        _chatHistory.Add(new UserChatMessage(toolResultsForHistory));
                        _chatHistory.Add(new AssistantChatMessage(assistantReply));
                    }
                    else
                    {
                        AppendToChat("System", "GPT ������ ��� �ֽ��ϴ�.");
                        // ��ü ���� �߰�
                        string fallbackReply = "���� ���� ����� Ȯ���߽��ϴ�.";
                        AppendToChat("ChatGPT", fallbackReply);
                        _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                    }
                }
                else
                {
                    AppendToChat("System", "���� ȣ�� �� GPT ������ �����ϴ�.");
                    // ��ü ���� �߰�
                    string fallbackReply = "������ ����Ǿ����ϴ�.";
                    AppendToChat("ChatGPT", fallbackReply);
                    _chatHistory.Add(new AssistantChatMessage(fallbackReply));
                }
            }
            catch (Exception ex)
            {
                AppendToChat("System", $"���� ���� ��û ����: {ex.Message}");
                AppendToChat("System", $"���� Ʈ���̽�: {ex.StackTrace}");
                HandleError("���� ȣ�� �� ���� ���� ��û ����", ex);

                // ���� �߻��� ��ü ���� �߰�
                string errorReply = "���� ���� �� ������ �޴µ� ������ �߻��߽��ϴ�.";
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

                // ä�� �̷¿� AI ���� �߰�
                _chatHistory.Add(new AssistantChatMessage(assistantReply));
            }
            else
            {
                AppendToChat("System", "GPT ������ �����ϴ�.");
            }
        }

        private void HandleError(string message, Exception ex)
        {
            string errorDetails = ex != null ? $"\n\n�� ����: {ex.Message}" : "";
            AppendToChat("Error", message + errorDetails);

            // �߿� ������ �޽��� �ڽ��ε� ǥ��
            if (ex != null && !(ex is FileNotFoundException || ex is JsonException))
            {
                MessageBox.Show(message + errorDetails, "����", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // �α� ���Ͽ� ���� ���
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
                // �α� �� ���� �߻� �� ���� (�α� ���а� ���ø����̼� ���࿡ ������ ���� �ʵ���)
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
                    MessageBox.Show("���� ��û�� ó�� ���Դϴ�. ��� ��ٷ��ּ���.", "ó�� ��", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    _isProcessingRequest = true;
                    SetStatus("ó�� ��...");
                    await HandleChatAsync();
                }
                catch (Exception ex)
                {
                    HandleError("ä�� ó�� �� ������ �߻��߽��ϴ�.", ex);
                }
                finally
                {
                    _isProcessingRequest = false;
                    SetStatus("�غ��");
                }
            }
        }

        private OpenAiConfig LoadOpenAiConfig(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"���� ������ ã�� �� �����ϴ�: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            var config = JsonConvert.DeserializeObject<OpenAiConfig>(json);

            if (string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(config.ModelID))
            {
                throw new InvalidOperationException("���� ���Ͽ� �ʼ� ����(ApiKey �Ǵ� ModelID)�� �����Ǿ����ϴ�.");
            }

            return config;
        }

        // MCP ������ OpenAI �������� ��ȯ�ϴ� �Լ� �߰�
        private List<ChatTool> ConvertMcpToolsToOpenAiTools(IList<McpClientTool> mcpTools)
        {
            var openAiTools = new List<ChatTool>();

            if (mcpTools == null || mcpTools.Count == 0)
            {
                AppendToChat("System", "��� ������ ������ �����ϴ�.");
                return null;
            }
            // MCP ������ OpenAI ���� ��ȯ�մϴ�.
            openAiTools = mcpTools.Select(tool =>
            {
                try
                {
                    // MCP ������ �Է� ��Ű���� JSON ���ڿ��� ��ȯ�մϴ�.
                    string rawJsonSchema = tool.ProtocolTool.InputSchema.GetRawText();

                    // JSON ���ڿ��� BinaryData�� ��ȯ�մϴ�.
                    var parameters = BinaryData.FromString(rawJsonSchema);

                    // OpenAI ���� �����մϴ�.
                    return ChatTool.CreateFunctionTool(
                        functionName: tool.Name,
                        functionDescription: tool.Description,
                        functionParameters: parameters
                    );
                }
                catch (Exception ex)
                {
                    AppendToChat("System", $"���� {tool.Name} ��ȯ ����: {ex.Message}");
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
                HandleError("MCP Ŭ���̾�Ʈ�� �ʱ�ȭ���� �ʾҽ��ϴ�.", null);
                return;
            }

            try
            {
                SetStatus("���� ��� �ҷ����� ��...");
                _tools = await _mcpClient.ListToolsAsync();

                if (_tools == null || _tools.Count == 0)
                {
                    AppendToChat("System", "��� ������ ������ �����ϴ�.");
                    return;
                }

                AppendToChat("System", $"{_tools.Count}���� ������ �ҷ��Խ��ϴ�:");

                foreach (var tool in _tools)
                {
                    AppendToChat("System", $"����: {tool.Name}");
                    AppendToChat("System", $"����: {tool.Description}");
                    AppendToChat("System", $"��Ű��: {tool.ProtocolTool.InputSchema}");
                }

                // MCP ������ OpenAI ���� ��ȯ�մϴ�.
                _openAiTools = _tools.Select(tool =>
                {
                    try
                    {
                        // MCP ������ �Է� ��Ű���� JSON ���ڿ��� ��ȯ�մϴ�.
                        string rawJsonSchema = tool.ProtocolTool.InputSchema.GetRawText();

                        // JSON ���ڿ��� BinaryData�� ��ȯ�մϴ�.
                        var parameters = BinaryData.FromString(rawJsonSchema);

                        // OpenAI ���� �����մϴ�.
                        return ChatTool.CreateFunctionTool(
                            functionName: tool.Name,
                            functionDescription: tool.Description,
                            functionParameters: parameters
                        );
                    }
                    catch (Exception ex)
                    {
                        AppendToChat("System", $"���� {tool.Name} ��ȯ ����: {ex.Message}");
                        return null;
                    }
                })
                .Where(tool => tool != null)
                .ToList();

                AppendToChat("System", $"{_openAiTools.Count}���� ������ OpenAI �������� ��ȯ�Ǿ����ϴ�.");
                SetStatus("�غ��");
            }
            catch (Exception ex)
            {
                HandleError("���� ����� �ҷ����� �� ������ �߻��߽��ϴ�.", ex);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // ���ҽ� ����
            try
            {
                // _mcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                LogError("MCP Ŭ���̾�Ʈ ���� �� ������ �߻��߽��ϴ�.", ex);
            }

            base.OnFormClosing(e);
        }
    }
}