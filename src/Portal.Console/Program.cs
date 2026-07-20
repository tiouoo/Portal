using MinecraftLaunch.Components.Installer;

var minecrafts = await VanillaInstaller.EnumerableMinecraftAsync();

var minecraft = minecrafts.First(x => x.McVersion == "1.21.1");

var installer = VanillaInstaller.Create(@"D:\Temp\mc", minecraft);

installer.ProgressChanged += (sender, x) =>
{
    Console.WriteLine(
        $"{x.Speed} {x.Progress * 100}% {x.StepName} {x.Status} {x.FinishedStepTaskCount}/{x.TotalStepTaskCount}");
};


await installer.InstallAsync();

var optifine = (await OptifineInstaller.EnumerableOptifineAsync("1.21.1")).First();

var optifineInstaller = OptifineInstaller.Create(
    @"D:\Temp\mc",
    @"D:\Minecraft\jdk\openjdk-25_windows-x64_bin\jdk-25\bin\java.exe",
    optifine
);

optifineInstaller.ProgressChanged += (sender, x) =>
{
    Console.WriteLine(
        $"{x.Speed} {x.Progress * 100}% {x.StepName} {x.Status} {x.FinishedStepTaskCount}/{x.TotalStepTaskCount}");
};

var minecraft1 = await optifineInstaller.InstallAsync();