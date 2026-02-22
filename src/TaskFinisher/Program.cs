using Anthropic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using TaskFinisher.Commands;
using TaskFinisher.Configuration;
using TaskFinisher.Services;
using TaskFinisher.Services.Interfaces;
using TaskFinisher.Tools;

var services = new ServiceCollection();

// Logging
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// Settings singleton - populated at runtime by CredentialService
services.AddSingleton<AppSettings>();

// Anthropic client - constructed lazily after settings are populated
services.AddSingleton<AnthropicClient>(sp =>
{
    var s = sp.GetRequiredService<AppSettings>();
    return new AnthropicClient { ApiKey = s.AnthropicApiKey };
});

// Services registered against their interfaces
services.AddSingleton<ICredentialService, CredentialService>();
services.AddSingleton<IFilesystemTools,   FilesystemTools>();
services.AddSingleton<IGitService,        GitService>();
services.AddSingleton<IGitHubService,     GitHubService>();
services.AddSingleton<IClaudeAgentService, ClaudeAgentService>();
services.AddSingleton<IIssueProcessor,    IssueProcessor>();

var registrar = new TypeRegistrar(services);
var app = new CommandApp<RunCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName("task-finisher");
    config.SetApplicationVersion("1.0.0");
    config.AddExample(["--repo", "owner/repo"]);
    config.AddExample(["--repo", "owner/repo", "--token", "ghp_xxx", "--anthropic-key", "sk-ant-xxx"]);
    config.AddExample(["--repo", "owner/repo", "--non-interactive"]);
});

return await app.RunAsync(args);

// ---------------------------------------------------------------------------
// Spectre.Console.Cli DI bridge
// ---------------------------------------------------------------------------

internal sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    public ITypeResolver Build() => new TypeResolver(services.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        services.AddSingleton(service, _ => factory());
}

internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) =>
        type is null ? null : provider.GetService(type);

    public void Dispose()
    {
        if (provider is IDisposable d) d.Dispose();
    }
}
