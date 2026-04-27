namespace AthenaCompanion.Tools;

internal sealed record AthenaToolResult(string Output, bool StopVoice, bool ContinueVoiceResponse)
{
    public static AthenaToolResult Continue(string output) => new(output, StopVoice: false, ContinueVoiceResponse: true);

    public static AthenaToolResult StopVoiceWithoutResponse(string output) =>
        new(output, StopVoice: true, ContinueVoiceResponse: false);
}
