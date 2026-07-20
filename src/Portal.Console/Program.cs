using MinecraftLaunch;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;

InitializeHelper.Initialize(settings => {
    settings.MaxThread = 256;
    settings.MaxFragment = 128;
    settings.MaxRetryCount = 4;
    settings.IsEnableMirror = false;
    settings.IsEnableFragment = false;
    settings.CurseForgeApiKey = "Your Curseforge API";
    settings.UserAgent = "MLTest/1.0";
});

var entry = (await VanillaInstaller.EnumerableMinecraftAsync())
    .First(x => x.Id == "1.21.8");

var installer = VanillaInstaller.Create(@"D:\Temp\mc", entry);
installer.ProgressChanged += (_, arg) =>
    Console.WriteLine($"{arg.StepName} - {arg.FinishedStepTaskCount}/{arg.TotalStepTaskCount} - {(arg.IsStepSupportSpeed ? $"{DefaultDownloader.FormatSize(arg.Speed, true)} - {arg.Progress * 100:F2}%" : $"{arg.Progress * 100:F2}%")}");

var minecraft = await installer.InstallAsync();
Console.WriteLine(minecraft.Id);

var entry1 = (await ForgeInstaller.EnumerableForgeAsync("1.21.8"))
    .First();

var installer1 = ForgeInstaller.Create(@"D:\Temp\mc",@"D:\Minecraft\jdk\openjdk-25_windows-x64_bin\jdk-25\bin\java.exe", entry1);
installer1.ProgressChanged += (_, arg) =>
    Console.WriteLine($"{arg.StepName} - {arg.FinishedStepTaskCount}/{arg.TotalStepTaskCount} - {(arg.IsStepSupportSpeed ? $"{DefaultDownloader.FormatSize(arg.Speed, true)} - {arg.Progress * 100:0.00}%" : $"{arg.Progress * 100:0.00}%")}");

var minecraft1 = await installer1.InstallAsync();
Console.WriteLine(minecraft1.Id);

Console.WriteLine("Done!");
Console.ReadKey();