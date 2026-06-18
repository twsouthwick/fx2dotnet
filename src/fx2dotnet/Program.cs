using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!(args is [string slnPath] && !string.IsNullOrWhiteSpace(slnPath) && File.Exists(slnPath)))
{
    Console.WriteLine("Usage: fx2dotnet [path to solution]");
    return;
}

var builder = Host.CreateApplicationBuilder();

builder.AddCopilot();

var app = builder.Build();

var solutionFullPath = Path.GetFullPath(slnPath);

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var result = app.Services.GetRequiredService<IFx2Dotnet>().AnalyzeAsync(solutionFullPath, lifetime.ApplicationStopping).GetAwaiter().GetResult();

Console.WriteLine("=========== RESULT ================");
Console.WriteLine(result);
