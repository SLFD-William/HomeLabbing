using System.Text.Json.Nodes;
using Voice.Commands;
using Xunit;

namespace Voice.Commands.Tests;

public class McpToolBridgeTests
{
    [Theory]
    [InlineData("render_project", "{}", true)]
    [InlineData("load_rig", "{}", true)]
    [InlineData("call_reascript_function", "{\"functionName\":\"DeleteTrack\"}", true)]
    [InlineData("call_reascript_function", "{\"functionName\":\"Main_SaveProject\"}", true)]
    [InlineData("call_reascript_function", "{\"functionName\":\"InsertTrackAtIndex\"}", false)]
    [InlineData("call_request", "{\"requestType\":\"StopStream\"}", true)]
    [InlineData("call_request", "{\"requestType\":\"GetSceneList\"}", false)]
    [InlineData("set_scene", "{}", false)]
    public void RiskyCallsNeedConfirmation(string tool, string json, bool risky) =>
        Assert.Equal(risky, McpToolBridge.IsRisky(tool, JsonNode.Parse(json)!.AsObject()));

    [Theory]
    [InlineData("list_reascript_functions", "{}", true)]
    [InlineData("call_reascript_function", "{\"functionName\":\"GetTrack\"}", true)]
    [InlineData("call_reascript_function", "{\"functionName\":\"InsertTrackAtIndex\"}", false)]
    [InlineData("call_request", "{\"requestType\":\"GetVersion\"}", true)]
    [InlineData("set_scene", "{}", false)]
    public void ReadOnlyCallsDoNotAnnounceAnAction(string tool, string json, bool readOnly) =>
        Assert.Equal(readOnly, McpToolBridge.IsReadOnly(tool, JsonNode.Parse(json)!.AsObject()));
}
