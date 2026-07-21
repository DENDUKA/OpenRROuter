using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using OpenRussiaRouter.BillingService.Application.Services;

namespace OpenRussiaRouter.BillingService.Tests.Application;

public class FxQuoteServiceTests
{
    /// <summary>
    /// CBR serves XML_daily.asp as windows-1251. .NET Core cannot decode that until the CodePages
    /// provider is registered, and <c>RefreshAsync</c> swallows the failure as a warning — so when
    /// this regressed, Billing quietly priced every top-up off the fallback rate (95 RUB/USD while
    /// the market was ~78) and under-credited users by ~18% with nothing failing.
    /// </summary>
    [Fact]
    public void Loading_FxQuoteService_registers_the_windows_1251_codepage()
    {
        RuntimeHelpers.RunClassConstructor(typeof(FxQuoteService).TypeHandle);

        var act = () => Encoding.GetEncoding("windows-1251");

        act.Should().NotThrow<ArgumentException>();
        Encoding.GetEncoding("windows-1251").Should().NotBeNull();
    }
}
