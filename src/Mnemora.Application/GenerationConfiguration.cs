namespace Mnemora.Application;

public sealed record GenerationConfigurationIssue(string Code, string Setting, string Message);

public static class GenerationConfiguration
{
    public static IReadOnlyList<GenerationConfigurationIssue> Inspect(GenerationOptions options)
    {
        var issues = new List<GenerationConfigurationIssue>();
        if (!options.Enabled)
            issues.Add(new("generation_disabled", "Enabled", "A geração está desativada no backend. Habilite MemoryPackGeneration__Enabled."));
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            issues.Add(new("api_key_missing", "ApiKey", "A chave Groq não foi carregada pela API. Configure MemoryPackGeneration__ApiKey no backend."));
        else if (options.ApiKey.Trim().Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            issues.Add(new("api_key_invalid", "ApiKey", "A chave Groq contém espaços ou quebras de linha no meio do valor. Corrija a chave no backend."));
        if (!string.Equals(options.Provider, "Groq", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("provider_unsupported", "Provider", "Use MemoryPackGeneration__Provider=Groq. Não há troca automática de provedor."));
        if (options.Model != "openai/gpt-oss-120b")
            issues.Add(new("model_unsupported", "Model", "Use MemoryPackGeneration__Model=openai/gpt-oss-120b."));

        Check(nameof(options.MaxUnits), options.MaxUnits, 1, 64);
        Check(nameof(options.MaxSourceBytes), options.MaxSourceBytes, 1, 512 * 1024);
        Check(nameof(options.MaxDocumentBytes), options.MaxDocumentBytes, 1, 2 * 1024 * 1024);
        Check(nameof(options.MaxReportBytes), options.MaxReportBytes, 1, 2 * 1024 * 1024);
        Check(nameof(options.MaxSessionBytes), options.MaxSessionBytes, 1, 8 * 1024 * 1024);
        Check(nameof(options.MaxGlobalBytes), options.MaxGlobalBytes, 1, 16 * 1024 * 1024);
        Check(nameof(options.MaxSessions), options.MaxSessions, 1, 2);
        Check(nameof(options.TimeoutSeconds), options.TimeoutSeconds, 1, 90);
        Check(nameof(options.IdleMinutes), options.IdleMinutes, 1, 30);
        Check(nameof(options.LifetimeMinutes), options.LifetimeMinutes, 1, 120);
        Check(nameof(options.MaxAttemptsPerUnit), options.MaxAttemptsPerUnit, 1, 2);
        Check(nameof(options.MaxCalls), options.MaxCalls, 1, 80);
        Check(nameof(options.MaxOutputTokens), options.MaxOutputTokens, 1, 2_500);
        Check(nameof(options.MaxInputTokens), options.MaxInputTokens, 1, 4_000);
        Check(nameof(options.MaxTokens), options.MaxTokens, 1, 180_000);
        Check(nameof(options.MinCallIntervalSeconds), options.MinCallIntervalSeconds, 0, 86_400);
        if (options.MaxGlobalBytes < options.MaxSessionBytes)
            issues.Add(new("memory_limits_inconsistent", "MaxGlobalBytes", "MaxGlobalBytes precisa ser maior ou igual a MaxSessionBytes."));
        return issues;

        void Check(string setting, int value, int minimum, int maximum)
        {
            if (value < minimum || value > maximum)
                issues.Add(new("limit_invalid", setting, $"MemoryPackGeneration__{setting} precisa estar entre {minimum} e {maximum}."));
        }
    }
}
