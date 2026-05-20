using OpenRussiaRouter.BuildingBlocks.Usage;

namespace OpenRussiaRouter.ApiGateway.Router;

public static class RouterUsageExtractor
{

    public static bool TryExtract(string json, out TokenUsage usage)
        => TokenUsageExtractor.TryExtractJson(json, out usage);
}
