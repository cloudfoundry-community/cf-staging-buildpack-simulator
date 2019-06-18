using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Tasks;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.IO.CompressionTasks;
using static Nuke.Common.IO.HttpTasks;
using static Nuke.Common.Tooling.ProcessTasks;

[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Stage);
    
    [Parameter()]
    readonly string[] Buildpacks;
    
    [Parameter("Directory that would be be 'pushed' to PCF. This is the source app")]
    readonly AbsolutePath PushDirectory;
    AbsolutePath DropletDirectory => RootDirectory / "artifacts";

    AbsolutePath ContainerPath => TemporaryDirectory / "container";
    AbsolutePath BuildDirectory => ContainerPath / "build";
    AbsolutePath DepsDirectory => ContainerPath / "deps";
    AbsolutePath CacheDirectory => ContainerPath / "cache";
    AbsolutePath ProfileDirectory => ContainerPath / "profile.d"; 
    AbsolutePath BuildpacksDirectory => ContainerPath / "buildpacks"; 


    Target Stage => _ => _
        .Requires(() => Buildpacks)
        .Requires(() => PushDirectory)
        .Executes(() =>
        {
            EnsureCleanDirectory(ContainerPath);
            EnsureExistingDirectory(DepsDirectory);
            EnsureExistingDirectory(CacheDirectory);
            EnsureExistingDirectory(ProfileDirectory);
            EnsureExistingDirectory(BuildpacksDirectory);

            CopyDirectoryRecursively(PushDirectory, BuildDirectory);
            for (int i = 0; i < Buildpacks.Length; i++)
            {
                var buildpackLoc = Buildpacks[i];
                var buildpackDir = BuildpacksDirectory / i.ToString();
                EnsureExistingDirectory(buildpackDir);
                if (buildpackLoc.StartsWith("http"))
                {
                    var buildpackHashName = GetMd5Hash(buildpackLoc) + ".zip";
                    if (!FileExists(TemporaryDirectory / buildpackHashName))
                    {
                        HttpDownloadFile(buildpackLoc, TemporaryDirectory / buildpackHashName);
                    }
                    Uncompress(TemporaryDirectory / buildpackHashName, buildpackDir);
                }
                else
                {
                    Uncompress(buildpackLoc, buildpackDir);
                }

                var envVars = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(x => x.Key.ToString(), x => x.Value.ToString());
                envVars.Add("CF_STACK","windows");
                
                RunLifecycle("supply", buildpackDir, envVars, i);
                
                if (i == Buildpacks.Length - 1)
                {
                    RunLifecycle("finalize", buildpackDir, envVars, i);
                }
            }
            EnsureCleanDirectory(DropletDirectory);
            CopyDirectoryRecursively(BuildDirectory, DropletDirectory / "app");
            CopyDirectoryRecursively(BuildDirectory, DropletDirectory / "profile.d");
            Logger.Block($"Droplet created in {DropletDirectory}");
        });

    void RunLifecycle(string lifecycle, AbsolutePath buildpackDir, Dictionary<string,string> envVars, int index)
    {
        var exePath = buildpackDir / "bin" / $"{lifecycle}.exe";
        if (FileExists(exePath))
        {
            StartProcess(exePath, 
                    arguments: $"{BuildDirectory} {CacheDirectory} {DepsDirectory} {index} {ProfileDirectory}", 
                    environmentVariables: envVars)
                .WaitForExit();
        }
        else
        {
            StartProcess("powershell", 
                    arguments: $"{buildpackDir / "bin/finalize.bat"} {BuildDirectory} {CacheDirectory} {DepsDirectory} {index} {ProfileDirectory}", 
                    environmentVariables: envVars)
                .WaitForExit();
        }
        
    }

    static string GetMd5Hash(string input)
    {
        var md5Hash = MD5.Create();
        // Convert the input string to a byte array and compute the hash.
        byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Create a new Stringbuilder to collect the bytes
        // and create a string.
        StringBuilder sBuilder = new StringBuilder();

        // Loop through each byte of the hashed data 
        // and format each one as a hexadecimal string.
        for (int i = 0; i < data.Length; i++)
        {
            sBuilder.Append(data[i].ToString("x2"));
        }

        // Return the hexadecimal string.
        return sBuilder.ToString();
    }
}
