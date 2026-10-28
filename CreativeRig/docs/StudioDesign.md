# Hybrid MCP Creative Workflow (.NET + DeepSeek Harness)  
**Design Document (with official API documentation links)**  
**Ready for Copilot CLI implementation**

---

## 1. Purpose and Scope

This document defines a **.NET-based MCP architecture** that exposes Reaper, Headrush, OBS, and CapCut as **MCP servers**, orchestrated by **DeepSeek Harness** for a **natural-language creative workflow**.

The system enables workflows such as:

> “Analyze the guitar track, design a dreamy tone, record a take, and create a CapCut short.”

---

## 2. Official API Documentation Links (All Products)

These links are authoritative and safe for Copilot CLI to crawl and use for implementation.

### 🎧 **Reaper API (Cockos)**
- **ReaScript API (Lua, Python, EEL2):**  
  `https://www.cockos.com/reaper/sdk/reascript/reascript.php` [(cockos.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.cockos.com%2Freaper%2Fsdk%2Freascript%2Freascript.php")  
- **Reaper C++ Extension API:**  
  `https://www.cockos.com/reaper/sdk/plugin/plugin.php` [(cockos.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.cockos.com%2Freaper%2Fsdk%2Fplugin%2Fplugin.php")  
- **JSFX / Video Processor Docs:**  
  `https://www.cockos.com/reaper/sdk/js/js.php` [(cockos.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.cockos.com%2Freaper%2Fsdk%2Fjs%2Fjs.php")  
- **SWS Extension API:**  
  [https://www.sws-extension.org/](https://www.sws-extension.org/)  
- **Community searchable API (X-Raym):**  
  `https://www.extremraym.com/cloud/reascript-doc/` [(extremraym.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.extremraym.com%2Fcloud%2Freascript-doc%2F")  

### 🎸 **Headrush API (Core Toolkit / Device Control)**
Headrush does not publish a single consolidated API reference, but the official control endpoints and SDKs are documented here:

- **Headrush Core Toolkit (official):**  
  [https://www.headrushfx.com/core/](https://www.headrushfx.com/core/)  
- **Headrush Pedalboard / Gigboard / Prime support docs:**  
  [https://www.headrushfx.com/support](https://www.headrushfx.com/support)  
- **Headrush Prime API reference (device control):**  
  [https://www.headrushfx.com/prime](https://www.headrushfx.com/prime)  

### 🎥 **OBS WebSocket API (Official)**
- **OBS WebSocket Protocol (official):**  
  `https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md` [(github.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fgithub.com%2Fobsproject%2Fobs-websocket%2Fblob%2Fmaster%2Fdocs%2Fgenerated%2Fprotocol.md")  
- **OBS WebSocket .NET Client:**  
  `https://github.com/obs-websocket-community-projects/obs-websocket-dotnet` [(github.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fgithub.com%2Fobs-websocket-community-projects%2Fobs-websocket-dotnet")  
- **OBS Studio API & plugin docs:**  
  [https://obsproject.com/docs/](https://obsproject.com/docs/)

### 🎬 **CapCut API**
CapCut’s API is provided through the **CapCutAPI MCP server** and official developer documentation:

- **CapCutAPI MCP Server (GitHub):**  
  [https://github.com/CapCutAPI/CapCutAPI](https://github.com/CapCutAPI/CapCutAPI)  
- **CapCut Developer Documentation:**  
  [https://developers.capcut.com/](https://developers.capcut.com/)  
- **CapCut OpenAPI Reference:**  
  [https://developers.capcut.com/openapi](https://developers.capcut.com/openapi)  

---

## 3. High-Level Architecture

```text
User (natural language)
        ↓
DeepSeek Harness (agent)
        ↓
MCP client plugin (@deepseek-ai/dsh-mcp-client)
        ↓
.NET MCP servers (Reaper / Headrush / OBS / CapCut)
        ↓
Reaper DAW / Headrush unit / OBS Studio / CapCut
```

---

## 4. .NET Solution Structure

```text
/CreativeRig.sln
  /Mcp.Core
  /Mcp.Reaper
  /Mcp.Headrush
  /Mcp.Obs
  /Mcp.CapCut
  /Shared
```

Each project is a standalone MCP server.

---

## 5. MCP Server Design (Common Pattern)

### 5.1 Base Contracts

```csharp
public sealed class McpToolDescriptor { ... }
public sealed class McpRequest { ... }
public sealed class McpResponse { ... }
```

### 5.2 Base Server (stdio)

```csharp
public abstract class McpServerBase
{
    protected abstract IEnumerable<McpToolDescriptor> GetTools();
    protected abstract Task<McpResponse> ExecuteAsync(McpRequest request, CancellationToken ct);

    public async Task RunStdioAsync(CancellationToken ct = default)
    {
        ...
    }
}
```

---

## 6. Reaper MCP Server (Hybrid Lua + Python)

### 6.1 Reaper API Links  
(For Copilot CLI to use during implementation)

- ReaScript API: `https://www.cockos.com/reaper/sdk/reascript/reascript.php` [(cockos.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.cockos.com%2Freaper%2Fsdk%2Freascript%2Freascript.php")  
- JSFX API: `https://www.cockos.com/reaper/sdk/js/js.php` [(cockos.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.cockos.com%2Freaper%2Fsdk%2Fjs%2Fjs.php")  
- SWS API: [https://www.sws-extension.org/](https://www.sws-extension.org/)  
- Community docs: `https://www.extremraym.com/cloud/reascript-doc/` [(extremraym.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fwww.extremraym.com%2Fcloud%2Freascript-doc%2F")

### 6.2 Responsibilities
- Transport control  
- Track/item editing  
- FX chain manipulation  
- Rendering  
- Audio analysis (Python + librosa)  
- Mood-based FX design

### 6.3 Tools
- `analyze_audio_region`  
- `apply_fx_chain_for_mood`  
- `render_project`  
- `start_recording`  
- `stop_recording`

---

## 7. Headrush MCP Server

### 7.1 Headrush API Links  
- Core Toolkit: [https://www.headrushfx.com/core/](https://www.headrushfx.com/core/)  
- Device docs: [https://www.headrushfx.com/prime](https://www.headrushfx.com/prime)  
- Support: [https://www.headrushfx.com/support](https://www.headrushfx.com/support)  

### 7.2 Responsibilities
- Rig loading  
- Block parameter control  
- Rig backup/restore

### 7.3 Tools
- `load_rig`  
- `set_block_parameter`  
- `backup_rig`

---

## 8. OBS MCP Server

### 8.1 OBS API Links  
- WebSocket Protocol:  
  `https://github.com/obsproject/obs-websocket/blob/master/docs/generated/protocol.md` [(github.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fgithub.com%2Fobsproject%2Fobs-websocket%2Fblob%2Fmaster%2Fdocs%2Fgenerated%2Fprotocol.md")  
- .NET Client:  
  `https://github.com/obs-websocket-community-projects/obs-websocket-dotnet` [(github.com in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fgithub.com%2Fobs-websocket-community-projects%2Fobs-websocket-dotnet")  
- OBS Studio Docs:  
  [https://obsproject.com/docs/](https://obsproject.com/docs/)

### 8.2 Responsibilities
- Scene switching  
- Source visibility  
- Recording/streaming control

### 8.3 Tools
- `set_scene`  
- `toggle_source`  
- `start_recording`  
- `stop_recording`

---

## 9. CapCut MCP Server

### 9.1 CapCut API Links  
- CapCutAPI MCP: [https://github.com/CapCutAPI/CapCutAPI](https://github.com/CapCutAPI/CapCutAPI)  
- Developer docs: [https://developers.capcut.com/](https://developers.capcut.com/)  
- OpenAPI: [https://developers.capcut.com/openapi](https://developers.capcut.com/openapi)  

### 9.2 Responsibilities
- Draft creation  
- Media insertion  
- Text overlays  
- Effects  
- Saving drafts

### 9.3 Tools
- `create_draft`  
- `add_video`  
- `add_text`  
- `save_draft`

---

## 10. Harness Integration (cordis.yml)

```yaml
bundles:
  - id: mcp-bridge
    plugins:
      - id: mcp-reaper
        name: '@deepseek-ai/dsh-mcp-client'
        config:
          serverName: reaper
          transport: stdio
          command: dotnet
          args: ['Mcp.Reaper.dll']

      - id: mcp-headrush
        name: '@deepseek-ai/dsh-mcp-client'
        config:
          serverName: headrush
          transport: http
          url: 'http://localhost:5001/mcp'

      - id: mcp-obs
        name: '@deepseek-ai/dsh-mcp-client'
        config:
          serverName: obs
          transport: http
          url: 'http://localhost:5002/mcp'

      - id: mcp-capcut
        name: '@deepseek-ai/dsh-mcp-client'
        config:
          serverName: capcut
          transport: http
          url: 'http://localhost:5003/mcp'
```

---

## 11. End-to-End Workflow Example

Harness orchestrates:

1. `mcp__reaper__analyze_audio_region`  
2. `mcp__headrush__set_block_parameter`  
3. `mcp__reaper__apply_fx_chain_for_mood`  
4. `mcp__obs__set_scene`  
5. `mcp__obs__start_recording`  
6. `mcp__reaper__start_recording`  
7. `mcp__reaper__render_project`  
8. `mcp__capcut__create_draft`  
9. `mcp__capcut__add_video`  
10. `mcp__capcut__add_text`  
11. `mcp__capcut__save_draft`

---

## 12. Copilot CLI Implementation Plan

Copilot CLI can now:

- Scaffold each MCP server  
- Implement bridges using the linked official APIs  
- Generate tool schemas  
- Build the stdio/HTTP transports  
- Wire everything into Harness
