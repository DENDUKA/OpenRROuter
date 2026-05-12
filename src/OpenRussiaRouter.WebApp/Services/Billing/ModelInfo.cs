namespace OpenRussiaRouter.WebApp.Services.Billing;

public sealed record ModelInfo(string Id, string DisplayName, string UpstreamModelId, string PriceVersion, decimal InputPrice, decimal OutputPrice, decimal CachePrice, bool Enabled);