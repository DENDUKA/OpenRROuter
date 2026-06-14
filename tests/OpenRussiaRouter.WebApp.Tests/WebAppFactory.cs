using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Http;

namespace OpenRussiaRouter.WebApp.Tests;

public class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // TS-010: tests hit the language-preference controller (and other
            // antiforgery-protected controllers) with raw form POSTs. Globally
            // disable antiforgery in the test host so we can exercise the handler
            // without scraping a token off a prior GET.
            services.AddControllers(options =>
            {
                options.Filters.Add(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
            });
            services.Configure<Microsoft.AspNetCore.Mvc.RazorPages.RazorPagesOptions>(options =>
            {
                options.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute());
            });
        });
    }
}