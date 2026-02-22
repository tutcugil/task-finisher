using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using TaskFinisher.Commands;
using TaskFinisher.Configuration;
using TaskFinisher.Services;
using TaskFinisher.Tools;

var services = new ServiceCollection();

// Core settings singleton - mutable POCO populated at runtime by CredentialService
services.AddSingleton<AppSettings>();

// Services
services.AddSingleton<CredentialService>();
services.AddSingleton<FilesystemTools>();

// GitHub and Git services are registered but re-instantiated after credentials are set
// (RunCommand creates fresh instances with populated AppSettings)
services.AddSingleton<GitHubService>(sp => new GitHubService(sp.GetRequiredService<AppSettings>()));
services.AddSingleton<GitService>();
services.AddSingleton<ClaudeAgentService>(sp =>
    new ClaudeAgentService(
        sp.GetRequiredService<AppSettings>(),
        sp.GetRequiredService<FilesystemTools>()));
services.AddSingleton<IssueProcessor>(sp =>
    new IssueProcessor(
        sp.GetRequiredService<AppSettings>(),
        sp.GetRequiredService<GitHubService>(),
        sp.GetRequiredService<GitService>(),
        sp.GetRequiredService<ClaudeAgentService>()));

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

internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services) => _services = services;

    public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());

    public void Register(Type service, Type implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        _services.AddSingleton(service, _ => factory());
}

internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider) => _provider = provider;

    public object? Resolve(Type? type) =>
        type is null ? null : _provider.GetService(type);

    public void Dispose()
    {
        if (_provider is IDisposable d) d.Dispose();
    }
}
