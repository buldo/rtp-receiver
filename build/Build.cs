using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.MinVer;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "continuous",
    GitHubActionsImage.UbuntuLatest,
    On = new[] { GitHubActionsTrigger.Push },
    InvokedTargets = new[] { nameof(Clean), nameof(PublishToNuget) },
    AutoGenerate = true,
    FetchDepth = 0,
    ImportSecrets = new[] { "CLOUDSMITH_API_KEY" })]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    public Build()
    {
        OutputPath = RootDirectory / "out";
    }

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(GenerateProjects = true)]
    readonly Solution Solution;

    [MinVer]
    readonly MinVer MinVer;

    readonly AbsolutePath OutputPath;

    Target Clean => _ => _
        .Executes(() =>
        {
            EnsureCleanDirectory(OutputPath);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(settings => settings
                .SetProjectFile(Solution.RtpReceiver));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(settings => settings
                .SetNoRestore(true)
                .SetProjectFile(Solution.RtpReceiver)
                .SetConfiguration(Configuration));
        });

    Target Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(settings => settings
                //.SetNoBuild(true)
                .SetProject(Solution.RtpReceiver)
                .SetOutputDirectory(OutputPath)
                .SetVersion(MinVer.PackageVersion));
        });

    Target PublishToNuget => _ => _
        .DependsOn(Publish)
        .Executes(() =>
        {
        });
}
