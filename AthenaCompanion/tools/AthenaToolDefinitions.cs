namespace AthenaCompanion.Tools;

internal static class AthenaToolDefinitions
{
    public static object[] Create(bool strict) =>
    [
        CreateInspectScreenTool(strict),
        CreateScreenImageTool(strict)
    ];

    private static object CreateInspectScreenTool(bool strict) =>
        strict
            ? new
            {
                type = "function",
                name = "inspect_screen",
                description = "Capture the user's current primary screen and answer a concise question about what is visible. Use only after the user explicitly asks about their screen.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        question = new
                        {
                            type = "string",
                            description = "The user's screen-related question."
                        }
                    },
                    required = new[] { "question" },
                    additionalProperties = false
                }
            }
            : new
            {
                type = "function",
                name = "inspect_screen",
                description = "Capture the user's current primary screen and answer a concise question about what is visible. Use only after the user explicitly asks about their screen.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        question = new
                        {
                            type = "string",
                            description = "The user's screen-related question."
                        }
                    },
                    required = new[] { "question" }
                }
            };

    private static object CreateScreenImageTool(bool strict) =>
        strict
            ? new
            {
                type = "function",
                name = "create_screen_image",
                description = "Capture the user's current primary screen, summarize it, generate an image such as an infographic with gpt-image-2, and open it in a lightbox. Use only after explicit user request.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        prompt = new
                        {
                            type = "string",
                            description = "The user's requested generated-image instruction."
                        }
                    },
                    required = new[] { "prompt" },
                    additionalProperties = false
                }
            }
            : new
            {
                type = "function",
                name = "create_screen_image",
                description = "Capture the user's current primary screen, summarize it, generate an image such as an infographic with gpt-image-2, and open it in a lightbox. Use only after explicit user request.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        prompt = new
                        {
                            type = "string",
                            description = "The user's requested generated-image instruction."
                        }
                    },
                    required = new[] { "prompt" }
                }
            };
}
