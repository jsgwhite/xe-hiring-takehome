using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RateAlerts.Api.Configuration;
using RateAlerts.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string FrontendDevCorsPolicy = "FrontendDev";

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();

builder.Services
    .AddOptions<XeOptions>()
    .Bind(builder.Configuration.GetSection(XeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Only used when the app starts without a Vite dev server proxying /api for it - e.g. hitting the
// API directly from a browser. The proxy path used by `npm run dev` never needs this.
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendDevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddHttpClient<XeRateProvider>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<XeOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);

    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountId}:{options.ApiKey}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
});

builder.Services.AddSingleton<IRateProvider>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<XeOptions>>().Value;

    // UseFakeRates is only honoured in Development - the sandbox key's rates are canned (see
    // FakeRateProvider), and this keeps a misconfigured setting from ever masking real data outside
    // a developer's own machine.
    IRateProvider inner = options.UseFakeRates && builder.Environment.IsDevelopment()
        ? new FakeRateProvider()
        : serviceProvider.GetRequiredService<XeRateProvider>();

    var cache = serviceProvider.GetRequiredService<IMemoryCache>();
    return new CachingRateProvider(inner, cache, serviceProvider.GetRequiredService<IOptions<XeOptions>>());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(FrontendDevCorsPolicy);
app.MapControllers();

app.Run();
