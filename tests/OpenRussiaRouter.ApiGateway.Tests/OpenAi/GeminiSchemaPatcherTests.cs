using System.Text.Json;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.OpenAi;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class GeminiSchemaPatcherTests
{
    [Fact]
    public void SanitizeIfGeminiModel_RemovesRefSiblingsInToolSchema()
    {
        const string request = """
        {
          "model": "gemini-test",
          "messages": [
            {
              "role": "user",
              "content": "QuestionPrompt should remain in plain text"
            }
          ],
          "tools": [
            {
              "type": "function",
              "function": {
                "name": "ask",
                "parameters": {
                  "type": "object",
                  "properties": {
                    "question": {
                      "$ref": "#/$defs/QuestionPrompt",
                      "description": "Prompt payload"
                    }
                  },
                  "$defs": {
                    "QuestionPrompt": {
                      "type": "object",
                      "properties": {
                        "text": { "type": "string" }
                      }
                    }
                  }
                }
              }
            }
          ]
        }
        """;

        var result = GeminiSchemaPatcher.SanitizeIfGeminiModel(request, "gemini-cli/gemini-3.1-pro-preview");

        using var document = JsonDocument.Parse(result);
        var questionSchema = document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .GetProperty("properties")
            .GetProperty("question");

        questionSchema.EnumerateObject().Select(property => property.Name)
            .Should().ContainSingle().Which.Should().Be("$ref");
        questionSchema.GetProperty("$ref").GetString().Should().Be("#/$defs/QuestionPrompt");

        document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .GetProperty("$defs")
            .GetProperty("QuestionPrompt")
            .GetProperty("type")
            .GetString()
            .Should().Be("object");
        document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()
            .Should().Be("QuestionPrompt should remain in plain text");
    }

    [Fact]
    public void SanitizeIfGeminiModel_LeavesNonGeminiRequestUnchanged()
    {
        const string request = """{"model":"gpt-test","tools":[{"function":{"parameters":{"properties":{"x":{"$ref":"#/$defs/X","description":"kept"}}}}}]}""";

        var result = GeminiSchemaPatcher.SanitizeIfGeminiModel(request, "gpt-4.1");

        result.Should().Be(request);
    }

    [Fact]
    public void SanitizeIfGeminiModel_RemovesRefSiblingsInResponseFormatSchema()
    {
        const string request = """
        {
          "model": "gemini-test",
          "response_format": {
            "type": "json_schema",
            "json_schema": {
              "name": "answer",
              "schema": {
                "type": "object",
                "properties": {
                  "answer": {
                    "ref": "Answer",
                    "nullable": true
                  }
                }
              }
            }
          }
        }
        """;

        var result = GeminiSchemaPatcher.SanitizeIfGeminiModel(request, "gemini-3.1-pro-preview");

        using var document = JsonDocument.Parse(result);
        var answerSchema = document.RootElement
            .GetProperty("response_format")
            .GetProperty("json_schema")
            .GetProperty("schema")
            .GetProperty("properties")
            .GetProperty("answer");

        answerSchema.EnumerateObject().Select(property => property.Name)
            .Should().ContainSingle().Which.Should().Be("ref");
        answerSchema.GetProperty("ref").GetString().Should().Be("Answer");
    }
}
