using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Microsoft.Playwright;

namespace RecipeManager.BrowserTests;

public sealed class BrowserTestApplication : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"recipe-manager-browser-tests-{Guid.NewGuid():N}.db");
    private readonly string imageStoragePath = Path.Combine(
        Path.GetTempPath(),
        $"recipe-manager-browser-images-{Guid.NewGuid():N}");
    private readonly StringBuilder processOutput = new();
    private Process? process;
    private IPlaywright? playwright;
    private IBrowser? browser;

    public string BaseUrl { get; private set; } = string.Empty;

    public string GetProcessOutput() => ReadOutput();

    public async Task InitializeAsync()
    {
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var repositoryRoot = FindRepositoryRoot();
        var webProject = Path.Combine(repositoryRoot, "src", "RecipeManager.Web", "RecipeManager.Web.csproj");
        var webAssembly = Path.Combine(AppContext.BaseDirectory, "RecipeManager.Web.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(webProject)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(webAssembly);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["RecipeBox__BrowserTestMode"] = "true";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={databasePath}";
        startInfo.Environment["RecipeImages__StoragePath"] = imageStoragePath;

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => AppendOutput(args.Data);
        process.ErrorDataReceived += (_, args) => AppendOutput(args.Data);
        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start the Recipe Box browser-test application.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await WaitForApplicationAsync();
    }

    public async Task<IBrowserContext> CreateContextAsync() =>
        await browser!.NewContextAsync();

    public async Task DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.CloseAsync();
        }

        playwright?.Dispose();

        if (process is not null)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process.Dispose();
        }

        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(imageStoragePath))
        {
            Directory.Delete(imageStoragePath, recursive: true);
        }
    }

    private async Task WaitForApplicationAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"Recipe Box browser-test application exited with code {process.ExitCode}.\n{ReadOutput()}");
            }

            try
            {
                using var response = await client.GetAsync(BaseUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Recipe Box did not start within 60 seconds.\n{ReadOutput()}");
    }

    private void AppendOutput(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (processOutput)
        {
            processOutput.AppendLine(line);
        }
    }

    private string ReadOutput()
    {
        lock (processOutput)
        {
            return processOutput.ToString();
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot()
    {
        var repositoryRoot = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "RecipeManagerRepositoryRoot")
            ?.Value;
        return repositoryRoot is not null
            ? Path.GetFullPath(repositoryRoot)
            : throw new DirectoryNotFoundException("The browser-test project does not define its repository root.");
    }
}
