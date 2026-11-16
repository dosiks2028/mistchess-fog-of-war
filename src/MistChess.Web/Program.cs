using MistChess.Web.Components;
using MistChess.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

// Vercel can start a fresh container between requests. Persist the same ASP.NET
// Data Protection key in every instance so antiforgery cookies remain readable.
var sharedDataProtectionKeyXml = builder.Configuration["MISTCHESS_DATA_PROTECTION_KEY_XML"];
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MistChess");

if (!string.IsNullOrWhiteSpace(sharedDataProtectionKeyXml))
{
    var keyElement = XElement.Parse(sharedDataProtectionKeyXml);
    if (keyElement.Name != "key" || keyElement.Attribute("id") is null)
    {
        throw new InvalidOperationException(
            "MISTCHESS_DATA_PROTECTION_KEY_XML must contain one ASP.NET Core Data Protection <key> element.");
    }

    var keyDirectory = Path.Combine(Path.GetTempPath(), "mistchess-data-protection");
    Directory.CreateDirectory(keyDirectory);
    File.WriteAllText(
        Path.Combine(keyDirectory, "key-shared.xml"),
        keyElement.ToString(SaveOptions.DisableFormatting));

    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddAntiforgery(options =>
{
    // Ignore cookies issued by older Vercel containers with ephemeral keys.
    options.Cookie.Name = "MistChess.Antiforgery.v2";
});
builder.Services.AddSingleton<OnlineGameRegistry>();
builder.Services.AddScoped<OnlineGameClient>();
builder.Services.AddScoped<GameSessionStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "MistChess" }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
