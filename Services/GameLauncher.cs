using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.LiteLoader;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.ProcessBuilder;

namespace BlackLaunch.Services;

public record DownloadProgress(int Percentage, string Speed, string Eta);

public class GameLauncher(string sharedPath)
{
    private static readonly HttpClient _httpClient = new();

    private readonly string _sharedPath = sharedPath;
    private Process? _runningGame;
    
    public bool IsRunning => _runningGame != null && !_runningGame.HasExited;
    
    public event Action<string>? StatusChanged;
    public event Action<DownloadProgress>? ProgressChanged;
    public event Action? ProgressFinished;
    public event Action? GameStarted;
    public event Action? GameExited;

    public void Stop() { if (IsRunning) try { _runningGame?.Kill(); } catch {} }

    public async Task LaunchAsync(string nickname, string mcVersion, string loader, string loaderVersion, string instancePath)
    {
        var path = new MinecraftPath() {
            BasePath = instancePath,
            Library = Path.Combine(_sharedPath, "libraries"),
            Versions = Path.Combine(_sharedPath, "versions"),
            Resource = Path.Combine(_sharedPath, "resources"),
            Assets = Path.Combine(_sharedPath, "assets"),
            Runtime = Path.Combine(_sharedPath, "runtime")
        };
        path.CreateDirs();
        
        var launcher = new MinecraftLauncher(path);
        var sw = Stopwatch.StartNew();
        var updateTimer = Stopwatch.StartNew();
        bool downloadStarted = false;
        launcher.FileProgressChanged += (sender, args) => {
            if (!downloadStarted) {
                downloadStarted = true;
                StatusChanged?.Invoke(i18n.Get("StatusDownloadingFiles"));
            }
            if (updateTimer.ElapsedMilliseconds > 300) StatusChanged?.Invoke(i18n.Get("StatusDownloading", args.Name ?? ""));
        };
        
        launcher.ByteProgressChanged += (sender, args) => {
            if (args.TotalBytes <= 0) return;
            if (updateTimer.ElapsedMilliseconds < 100 && args.ProgressedBytes != args.TotalBytes) return;
            updateTimer.Restart();
 
            int percentage = (int)((args.ProgressedBytes * 100) / args.TotalBytes);
            double totalSeconds = sw.Elapsed.TotalSeconds;
            string speedStr = "0 MB/s";
            string etaStr = "00:00";
            if (totalSeconds > 0) {
                double bytesPerSec = args.ProgressedBytes / totalSeconds;
                speedStr = (bytesPerSec / 1024 / 1024).ToString("0.00") + " MB/s";
                long remainingBytes = args.TotalBytes - args.ProgressedBytes;
                if (bytesPerSec > 0) {
                    TimeSpan eta = TimeSpan.FromSeconds(remainingBytes / bytesPerSec);
                    etaStr = eta.ToString(@"mm\:ss");
                }
            }
            ProgressChanged?.Invoke(new DownloadProgress(percentage, speedStr, etaStr));
        };
        
        string versionToLaunch = mcVersion;
        StatusChanged?.Invoke(i18n.Get("StatusPreparingLoader", loader));
        if (loader == "Fabric") {
            var fabricInstaller = new FabricInstaller(_httpClient);
            if (string.IsNullOrEmpty(loaderVersion))
                versionToLaunch = await fabricInstaller.Install(mcVersion, path);
            else
                versionToLaunch = await fabricInstaller.Install(mcVersion, loaderVersion, path);
        } else if (loader == "Forge") {
            var forgeInstaller = new ForgeInstaller(launcher);
            if (string.IsNullOrEmpty(loaderVersion))
                versionToLaunch = await forgeInstaller.Install(mcVersion);
            else
                versionToLaunch = await forgeInstaller.Install(mcVersion, loaderVersion);
        } else if (loader == "Quilt") {
            var quiltInstaller = new QuiltInstaller(_httpClient);
            if (string.IsNullOrEmpty(loaderVersion))
                versionToLaunch = await quiltInstaller.Install(mcVersion, path);
            else
                versionToLaunch = await quiltInstaller.Install(mcVersion, loaderVersion, path);
        } else if (loader == "NeoForge") {
            var neoForgeInstaller = new NeoForgeInstaller(launcher);
            if (string.IsNullOrEmpty(loaderVersion))
                versionToLaunch = await neoForgeInstaller.Install(mcVersion);
            else
                versionToLaunch = await neoForgeInstaller.Install(mcVersion, loaderVersion);
        } else if (loader == "LiteLoader") {
            var liteLoaderInstaller = new LiteLoaderInstaller(_httpClient);
            var loaders = await liteLoaderInstaller.GetAllLiteLoaders();
            var loaderToInstall = (string.IsNullOrEmpty(loaderVersion)
                ? loaders.FirstOrDefault(l => l.BaseVersion == mcVersion)
                : loaders.FirstOrDefault(l => l.BaseVersion == mcVersion && l.Version == loaderVersion))
                ?? throw new Exception(i18n.Get("ErrorLiteLoader", mcVersion));
            var baseVersion = await launcher.GetVersionAsync(mcVersion);
            versionToLaunch = await liteLoaderInstaller.Install(loaderToInstall, baseVersion, path);
        }
        
        sw.Restart();
        updateTimer.Restart();
        StatusChanged?.Invoke(i18n.Get("StatusCheckingAssets"));
        await launcher.InstallAsync(versionToLaunch);
        ProgressFinished?.Invoke();
        StatusChanged?.Invoke(i18n.Get("StatusBuildingFiles"));
        
        var jvmArguments = "-XX:+UseG1GC -Dsun.rmi.dgc.server.gcInterval=2147483646 -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M -Dfile.encoding=UTF-8";
        if (versionToLaunch == "1.16.4" || versionToLaunch == "1.16.5") {
            jvmArguments += " -Dminecraft.api.env=custom -Dminecraft.api.auth.host=https://invalid.invalid -Dminecraft.api.account.host=https://invalid.invalid -Dminecraft.api.session.host=https://invalid.invalid -Dminecraft.api.services.host=https://invalid.invalid";
        }
        var arguments = new MArgument[] { MArgument.FromCommandLine(jvmArguments) };
        var launchOptions = new MLaunchOption {
            Session = MSession.CreateOfflineSession(nickname),
            MaximumRamMb = 4096,
            MinimumRamMb = 1024,
            FullScreen = false,
            ExtraJvmArguments = arguments
        };
        var process = await launcher.BuildProcessAsync(versionToLaunch, launchOptions);
        _runningGame = process;
        _runningGame.EnableRaisingEvents = true;
        _runningGame.Exited += (s, ev) => {
            _runningGame = null;
            GameExited?.Invoke();
        };
        GameStarted?.Invoke();
        var processWrapper = new ProcessWrapper(process);
        processWrapper.OutputReceived += (s, log) => Console.WriteLine($"[MINECRAFT] {log}");
        processWrapper.StartWithEvents();
    }
}
