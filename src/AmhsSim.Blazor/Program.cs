using AmhsSim.Blazor.Components;
using AmhsSim.Engine;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddAmhsLogging("blazor");
builder.Services.AddAmhsEngine(builder.Configuration);

builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

app.Services.MigrateAmhsDatabase();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();