using System.Text;
using CyberPipe.Contracts;

namespace CyberPipe.Skills.Knowledge.Services;

/// <summary>
/// Провайдер контекста базы знаний для LLM (TASK-053).
/// Возвращает наиболее релевантные фрагменты с источниками, строго укладываясь в бюджет токенов.
/// </summary>
public sealed class KnowledgeContextProvider : IKnowledgeContextProvider
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly KnowledgeOptions _options;

    public KnowledgeContextProvider(
        IKnowledgeService knowledgeService,
        KnowledgeOptions? options = null)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _options = options ?? new KnowledgeOptions();
    }

    public async Task<string> GetContextAsync(string query, int tokenBudget, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        int effectiveBudget = tokenBudget > 0 ? tokenBudget : _options.DefaultTokenBudget;
        // Conservative heuristic: ~3.5 chars per token for multilingual text
        int maxCharBudget = (int)(effectiveBudget * 3.5);

        var searchReq = new KnowledgeSearchRequest
        {
            Query = query,
            TopK = _options.MaxContextFragments,
            MinScore = _options.DefaultMinScore
        };

        IReadOnlyList<KnowledgeHit> hits = await _knowledgeService.SearchAsync(searchReq, ct).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        int usedChars = 0;

        foreach (KnowledgeHit hit in hits)
        {
            ct.ThrowIfCancellationRequested();

            string header = $"[Source: {hit.Source}, Score: {hit.Score:F2}]";
            string fragment = $"{header}\n{hit.ChunkText}\n";

            if (usedChars + fragment.Length > maxCharBudget)
            {
                int remaining = maxCharBudget - usedChars;
                if (remaining > header.Length + 20)
                {
                    string truncatedText = hit.ChunkText.Substring(0, remaining - header.Length - 10).TrimEnd() + "...";
                    sb.AppendLine($"{header}\n{truncatedText}");
                }
                break;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine("---");
                usedChars += 4;
            }

            sb.Append(fragment);
            usedChars += fragment.Length;
        }

        return sb.ToString().Trim();
    }
}
