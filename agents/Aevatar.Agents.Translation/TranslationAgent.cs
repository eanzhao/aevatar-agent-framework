using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Abstractions;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;
using System.IO;
using Aevatar.Agents.AI;

namespace Aevatar.Agents.Translation;

public class TranslationAgent : AIGAgentBase<TranslationState, TranslationConfig>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize State
        if (string.IsNullOrEmpty(CustomState.Id))
        {
            CustomState.Id = Id.ToString();
            CustomState.FilesProcessedCount = 0;
            CustomState.TotalCharsTranslated = 0;
        }

        CustomState.LastActivityAt = Timestamp.FromDateTime(DateTime.UtcNow);

        // Initialize Config Defaults
        if (string.IsNullOrEmpty(CustomConfig.DefaultTargetLanguage))
        {
            CustomConfig.DefaultTargetLanguage = "en";
        }

        // Setup System Prompt for translation expert
        SystemPrompt = "You are a professional translator. " +
                       "Your task is to translate the provided text content accurately and naturally into the target language. " +
                       "Do not include any explanations or conversational filler, just output the translated text.";

        Logger.LogInformation("TranslationAgent {AgentId} activated. Default target: {Target}", Id,
            CustomConfig.DefaultTargetLanguage);
    }

    [EventHandler(AllowSelfHandling = true)]
    public async Task HandleTranslateFile(TranslateFileEvent evt)
    {
        Logger.LogInformation("Received translation request for file: {FilePath}", evt.FilePath);

        CustomState.LastActivityAt = Timestamp.FromDateTime(DateTime.UtcNow);

        string targetLang = !string.IsNullOrEmpty(evt.TargetLanguage)
            ? evt.TargetLanguage
            : CustomConfig.DefaultTargetLanguage;

        try
        {
            // 1. Validate File
            if (!File.Exists(evt.FilePath))
            {
                throw new FileNotFoundException($"File not found: {evt.FilePath}");
            }

            // 2. Read Content
            string content = await File.ReadAllTextAsync(evt.FilePath);
            if (string.IsNullOrWhiteSpace(content))
            {
                Logger.LogWarning("File is empty: {FilePath}", evt.FilePath);
                return;
            }

            // 3. Perform Translation using AI
            Logger.LogInformation("Translating {Chars} chars to {Lang}...", content.Length, targetLang);

            // Construct a specific prompt for this task
            var prompt = $"Translate the following text to {targetLang}:\n\n{content}";

            var response = await ChatAsync(new ChatRequest
            {
                Message = prompt,
                RequestId = Guid.NewGuid().ToString()
            });

            string translatedText = response.Content;

            // 4. Write Output
            string directory = Path.GetDirectoryName(evt.FilePath) ?? "";
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(evt.FilePath);
            string ext = Path.GetExtension(evt.FilePath);
            string outputFileName = $"{fileNameWithoutExt}_{targetLang}{ext}";
            string outputPath = Path.Combine(directory, outputFileName);

            if (File.Exists(outputPath) && !CustomConfig.OverwriteExisting)
            {
                Logger.LogWarning("Output file already exists and overwrite is disabled: {OutputPath}", outputPath);
                // Optionally publish failure event or skip
            }
            else
            {
                await File.WriteAllTextAsync(outputPath, translatedText);
                Logger.LogInformation("Translation saved to: {OutputPath}", outputPath);

                // 5. Update State
                CustomState.FilesProcessedCount++;
                CustomState.TotalCharsTranslated += content.Length;

                // 6. Publish Success
                await PublishAsync(new FileTranslatedEvent
                {
                    OriginalFilePath = evt.FilePath,
                    OutputFilePath = outputPath,
                    TargetLanguage = targetLang,
                    Success = true,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Translation failed for {FilePath}", evt.FilePath);

            await PublishAsync(new FileTranslatedEvent
            {
                OriginalFilePath = evt.FilePath,
                TargetLanguage = targetLang,
                Success = false,
                ErrorMessage = ex.Message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Translator ({CustomState.FilesProcessedCount} files processed)");
    }
}