using System.Text.Json;
using FluentAssertions;
using OpenRussiaRouter.ApiGateway.OpenAi;

namespace OpenRussiaRouter.ApiGateway.Tests.OpenAi;

public sealed class GeminiSchemaPatcherTests
{
    [Fact]
    public void SanitizeIfGeminiModel_InlinesRefInToolSchema()
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

        questionSchema.TryGetProperty("$ref", out _).Should().BeFalse();
        questionSchema.GetProperty("type").GetString().Should().Be("object");
        questionSchema.GetProperty("description").GetString().Should().Be("Prompt payload");
        questionSchema.GetProperty("properties").GetProperty("text").GetProperty("type").GetString()
            .Should().Be("string");
        document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("parameters")
            .TryGetProperty("$defs", out _)
            .Should().BeFalse();
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
    public void SanitizeIfGeminiModel_InlinesRefInResponseFormatSchema()
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
                    "ref": "#/defs/Answer",
                    "nullable": true
                  }
                },
                "defs": {
                  "Answer": {
                    "type": "object",
                    "properties": {
                      "text": { "type": "string" }
                    }
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

        answerSchema.TryGetProperty("ref", out _).Should().BeFalse();
        answerSchema.GetProperty("type").GetString().Should().Be("object");
        answerSchema.GetProperty("nullable").GetBoolean().Should().BeTrue();
        answerSchema.GetProperty("properties").GetProperty("text").GetProperty("type").GetString()
            .Should().Be("string");
    }

    [Fact]
    public void SanitizeIfGeminiModel_InlinesNestedRefsFromDefinitions()
    {
        const string request = """
        {
          "model": "gemini-test",
          "tools": [
            {
              "name": "ask_question",
              "input_schema": {
                "type": "object",
                "properties": {
                  "prompt": {
                    "$ref": "#/definitions/QuestionPrompt",
                    "description": "Prompt"
                  }
                },
                "definitions": {
                  "QuestionPrompt": {
                    "type": "object",
                    "properties": {
                      "options": {
                        "type": "array",
                        "items": {
                          "$ref": "#/definitions/QuestionOption",
                          "description": "Option"
                        }
                      }
                    }
                  },
                  "QuestionOption": {
                    "type": "object",
                    "properties": {
                      "label": { "type": "string" },
                      "value": { "type": "string" }
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
        var inputSchema = document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("input_schema");
        var promptSchema = inputSchema
            .GetProperty("properties")
            .GetProperty("prompt");
        var optionSchema = promptSchema
            .GetProperty("properties")
            .GetProperty("options")
            .GetProperty("items");

        inputSchema.TryGetProperty("definitions", out _).Should().BeFalse();
        promptSchema.TryGetProperty("$ref", out _).Should().BeFalse();
        promptSchema.GetProperty("description").GetString().Should().Be("Prompt");
        optionSchema.TryGetProperty("$ref", out _).Should().BeFalse();
        optionSchema.GetProperty("description").GetString().Should().Be("Option");
        optionSchema.GetProperty("properties").GetProperty("label").GetProperty("type").GetString()
            .Should().Be("string");
    }

    [Fact]
    public void SanitizeIfGeminiModel_InlinesRefsWhenSchemaContainerIsUnknown()
    {
        const string request = """
        {
          "model": "gemini-test",
          "$defs": {
            "QuestionOption": {
              "type": "object",
              "properties": {
                "label": { "type": "string" }
              }
            }
          },
          "tools": [
            {
              "type": "function",
              "function": {
                "name": "ask",
                "custom_payload": {
                  "choices": [
                    {
                      "$ref": "#/$defs/QuestionOption",
                      "title": "Choice"
                    }
                  ]
                }
              }
            }
          ]
        }
        """;

        var result = GeminiSchemaPatcher.SanitizeIfGeminiModel(request, "gc/gemini-3.1-pro-preview");

        using var document = JsonDocument.Parse(result);
        var optionSchema = document.RootElement
            .GetProperty("tools")[0]
            .GetProperty("function")
            .GetProperty("custom_payload")
            .GetProperty("choices")[0];

        optionSchema.TryGetProperty("$ref", out _).Should().BeFalse();
        optionSchema.GetProperty("title").GetString().Should().Be("Choice");
        optionSchema.GetProperty("properties").GetProperty("label").GetProperty("type").GetString()
            .Should().Be("string");
    }
}
