using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

public static class RouterUsageExtractor
{

    public static bool TryExtract(string body, out TokenUsage usage)
    {
        if (TokenUsageExtractor.TryExtractJson(body, out usage) && usage.TotalTokens > 0)
            return true;
        return TokenUsageExtractor.TryExtractStreamingSse(body, out usage);
    }
}
