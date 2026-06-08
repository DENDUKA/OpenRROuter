using System.Globalization;
using System.Text;
using System.Xml;

namespace OpenRussiaRouter.WebApp.Services.Currency;

public class CbrClient
{
    private const string Url = "https://www.cbr.ru/scripts/XML_daily.asp";
    private const string UsdId = "R01235";
    private const string EurId = "R01239";

    private readonly IHttpClientFactory _httpClientFactory;

    static CbrClient()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public CbrClient(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public virtual async Task<(decimal rubPerUsd, decimal rubPerEur)> FetchRatesAsync(CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient("Cbr");
        var xml = await http.GetStringAsync(Url, ct);
        return ParseXml(xml);
    }

    internal static (decimal rubPerUsd, decimal rubPerEur) ParseXml(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var rubPerUsd = ParseValute(doc, UsdId);
        var rubPerEur = ParseValute(doc, EurId);
        return (rubPerUsd, rubPerEur);
    }

    private static decimal ParseValute(XmlDocument doc, string id)
    {
        var node = doc.SelectSingleNode($"//Valute[@ID='{id}']")
                   ?? throw new InvalidOperationException($"Valute {id} not found in CBR XML");
        var nominal = int.Parse(node.SelectSingleNode("Nominal")!.InnerText.Trim(), CultureInfo.InvariantCulture);
        var valueStr = node.SelectSingleNode("Value")!.InnerText.Trim().Replace(',', '.');
        var value = decimal.Parse(valueStr, CultureInfo.InvariantCulture);
        return value / nominal;
    }
}
