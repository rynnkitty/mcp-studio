//사용한 MCP 서버
1. npm i @modelcontextprotocol/server-everything
   https://www.npmjs.com/package/@modelcontextprotocol/server-everything
   
2. npm i mcp-image-reader
   https://www.npmjs.com/package/mcp-image-reader
   
3. npm i @modelcontextprotocol/server-filesystem
   https://www.npmjs.com/package/@modelcontextprotocol/server-filesystem


//mcp_config.json
{
  "mcpServers": {
    "everything": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-everything"
      ]
    },
    "image_reader": {
      "command": "npx",
      "args": [ "-y", "mcp-image-reader" ]
    },
    "filesystem": {
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-filesystem",
        "/Users/2106001/test"
      ]
    }
  }
}

//SDK

OpenAI SDK 2.1.0

//
config.json 파일에 ApiKey를 입력해야함.

{
  "ModelID": "gpt-3.5-turbo",
  "ApiKey": "<당신의 API KEY>",
}
