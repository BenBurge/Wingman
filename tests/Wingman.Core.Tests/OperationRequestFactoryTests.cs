using Wingman.Core.Bundles;
using Wingman.Core.Models;
using Wingman.Core.Operations;
using Wingman.Core.Settings;
using Wingman.Core.Winget;

namespace Wingman.Core.Tests;

public class OperationRequestFactoryTests
{
    private static readonly PackageRow Row = new("Git", "Git.Git", "2.45.0", "2.46.2", "winget");

    private static WingmanSettings DefaultSettings() => new();

    private static string[] Argv(OperationKind kind, OperationRequest request) => kind switch
    {
        OperationKind.Install => WingetArguments.Install(request),
        OperationKind.Upgrade => WingetArguments.Upgrade(request),
        OperationKind.Uninstall => WingetArguments.Uninstall(request),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Fact]
    public void Create_Defaults_ProducesPhaseOneInstallArgv()
    {
        string[] expected =
        [
            "install", "--id", "Git.Git", "--exact",
            "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), new InstallOptions());

        Assert.Equal("Git.Git", plan.Request.Id);
        Assert.Equal(expected, WingetArguments.Install(plan.Request));
    }

    [Fact]
    public void Create_Defaults_ProducesPhaseOneUpgradeArgv()
    {
        string[] expected =
        [
            "upgrade", "--id", "Git.Git", "--exact",
            "--source", "winget",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), new InstallOptions());

        Assert.Equal(expected, WingetArguments.Upgrade(plan.Request));
    }

    [Fact]
    public void Create_Defaults_ProducesPhaseOneUninstallArgv()
    {
        string[] expected =
        [
            "uninstall", "--id", "Git.Git", "--exact",
            "--disable-interactivity", "--accept-source-agreements",
        ];

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), new InstallOptions());

        Assert.Equal(expected, WingetArguments.Uninstall(plan.Request));
    }

    [Fact]
    public void Create_Install_VersionFromOptions_SetsFieldAndArgv()
    {
        var options = new InstallOptions { Version = "2.46.2" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.Equal("2.46.2", plan.Request.Version);
        Assert.Contains("--version", Argv(OperationKind.Install, plan.Request));
        Assert.Contains("2.46.2", Argv(OperationKind.Install, plan.Request));
    }

    [Fact]
    public void Create_Upgrade_VersionFromOptions_IsIgnored()
    {
        var options = new InstallOptions { Version = "2.46.2" };

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), options);

        Assert.Null(plan.Request.Version);
        Assert.DoesNotContain("--version", Argv(OperationKind.Upgrade, plan.Request));
    }

    [Fact]
    public void Create_Uninstall_VersionFromOptions_IsIgnored()
    {
        var options = new InstallOptions { Version = "2.46.2" };

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), options);

        Assert.Null(plan.Request.Version);
        Assert.DoesNotContain("--version", Argv(OperationKind.Uninstall, plan.Request));
    }

    [Theory]
    [InlineData(OperationKind.Install)]
    [InlineData(OperationKind.Upgrade)]
    [InlineData(OperationKind.Uninstall)]
    public void Create_ScopeFromOptions_SetsFieldAndArgv(OperationKind kind)
    {
        var options = new InstallOptions { InstallationScope = "user" };

        var plan = OperationRequestFactory.Create(kind, Row, DefaultSettings(), options);

        Assert.Equal("user", plan.Request.Scope);
        Assert.Contains("--scope", Argv(kind, plan.Request));
        Assert.Contains("user", Argv(kind, plan.Request));
    }

    [Theory]
    [InlineData(OperationKind.Install)]
    [InlineData(OperationKind.Upgrade)]
    [InlineData(OperationKind.Uninstall)]
    public void Create_ScopeFromSettings_AppliesWhenOptionsScopeEmpty(OperationKind kind)
    {
        var settings = new WingmanSettings { DefaultScope = "machine" };

        var plan = OperationRequestFactory.Create(kind, Row, settings, new InstallOptions());

        Assert.Equal("machine", plan.Request.Scope);
        Assert.Contains("--scope", Argv(kind, plan.Request));
    }

    [Fact]
    public void Create_ScopeFromOptions_WinsOverSettings()
    {
        var settings = new WingmanSettings { DefaultScope = "machine" };
        var options = new InstallOptions { InstallationScope = "user" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, settings, options);

        Assert.Equal("user", plan.Request.Scope);
    }

    [Fact]
    public void Create_Install_ArchitectureFromOptions_SetsFieldAndArgv()
    {
        var options = new InstallOptions { Architecture = "x64" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.Equal("x64", plan.Request.Architecture);
        Assert.Contains("--architecture", Argv(OperationKind.Install, plan.Request));
    }

    [Fact]
    public void Create_Upgrade_ArchitectureFromOptions_SetsFieldAndArgv()
    {
        var options = new InstallOptions { Architecture = "x64" };

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), options);

        Assert.Equal("x64", plan.Request.Architecture);
        Assert.Contains("--architecture", Argv(OperationKind.Upgrade, plan.Request));
    }

    [Fact]
    public void Create_Uninstall_ArchitectureFromOptions_IsDropped()
    {
        var options = new InstallOptions { Architecture = "x64" };

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), options);

        Assert.Null(plan.Request.Architecture);
        Assert.DoesNotContain("--architecture", Argv(OperationKind.Uninstall, plan.Request));
    }

    [Theory]
    [InlineData(OperationKind.Install)]
    [InlineData(OperationKind.Upgrade)]
    [InlineData(OperationKind.Uninstall)]
    public void Create_Interactive_SetsFieldAndArgv(OperationKind kind)
    {
        var options = new InstallOptions { InteractiveInstallation = true };

        var plan = OperationRequestFactory.Create(kind, Row, DefaultSettings(), options);

        Assert.True(plan.Request.Interactive);
        Assert.Contains("--interactive", Argv(kind, plan.Request));
    }

    [Fact]
    public void Create_Install_SkipHashCheck_SetsFieldAndArgv()
    {
        var options = new InstallOptions { SkipHashCheck = true };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.True(plan.Request.SkipHashCheck);
        Assert.Contains("--ignore-security-hash", Argv(OperationKind.Install, plan.Request));
    }

    [Fact]
    public void Create_Upgrade_SkipHashCheck_SetsFieldAndArgv()
    {
        var options = new InstallOptions { SkipHashCheck = true };

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), options);

        Assert.True(plan.Request.SkipHashCheck);
        Assert.Contains("--ignore-security-hash", Argv(OperationKind.Upgrade, plan.Request));
    }

    [Fact]
    public void Create_Uninstall_SkipHashCheck_IsAlwaysFalse()
    {
        var options = new InstallOptions { SkipHashCheck = true };

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), options);

        Assert.False(plan.Request.SkipHashCheck);
        Assert.DoesNotContain("--ignore-security-hash", Argv(OperationKind.Uninstall, plan.Request));
    }

    [Theory]
    [InlineData(OperationKind.Install)]
    [InlineData(OperationKind.Upgrade)]
    [InlineData(OperationKind.Uninstall)]
    public void Create_Force_IsAlwaysFalse(OperationKind kind)
    {
        var plan = OperationRequestFactory.Create(kind, Row, DefaultSettings(), new InstallOptions());

        Assert.False(plan.Request.Force);
        Assert.DoesNotContain("--force", Argv(kind, plan.Request));
    }

    [Fact]
    public void Create_Install_CustomArguments_UsesInstallList()
    {
        var options = new InstallOptions { CustomParameters_Install = ["--log", "C:\\temp\\git.log"] };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.Equal(["--log", "C:\\temp\\git.log"], plan.Request.CustomArguments);
        Assert.Contains("--log", Argv(OperationKind.Install, plan.Request));
    }

    [Fact]
    public void Create_Upgrade_CustomArguments_UsesUpdateList()
    {
        var options = new InstallOptions { CustomParameters_Update = ["--silent"] };

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), options);

        Assert.Equal(["--silent"], plan.Request.CustomArguments);
        Assert.Contains("--silent", Argv(OperationKind.Upgrade, plan.Request));
    }

    [Fact]
    public void Create_Uninstall_CustomArguments_UsesUninstallList()
    {
        var options = new InstallOptions { CustomParameters_Uninstall = ["--purge"] };

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), options);

        Assert.Equal(["--purge"], plan.Request.CustomArguments);
        Assert.Contains("--purge", Argv(OperationKind.Uninstall, plan.Request));
    }

    [Theory]
    [InlineData(OperationKind.Install)]
    [InlineData(OperationKind.Upgrade)]
    [InlineData(OperationKind.Uninstall)]
    public void Create_EmptyCustomArgumentsList_IsNull(OperationKind kind)
    {
        var plan = OperationRequestFactory.Create(kind, Row, DefaultSettings(), new InstallOptions());

        Assert.Null(plan.Request.CustomArguments);
    }

    [Fact]
    public void Create_RunAsAdministrator_RequiresElevation()
    {
        var options = new InstallOptions { RunAsAdministrator = true };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.True(plan.RequiresElevation);
    }

    [Fact]
    public void Create_MachineScopeFromOptions_RequiresElevation()
    {
        var options = new InstallOptions { InstallationScope = "machine" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.True(plan.RequiresElevation);
    }

    [Fact]
    public void Create_MachineScopeFromSettings_RequiresElevation()
    {
        var settings = new WingmanSettings { DefaultScope = "machine" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, settings, new InstallOptions());

        Assert.True(plan.RequiresElevation);
    }

    [Fact]
    public void Create_NeitherAdminNorMachineScope_DoesNotRequireElevation()
    {
        var settings = new WingmanSettings { DefaultScope = "user" };
        var options = new InstallOptions { InstallationScope = "" };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, settings, options);

        Assert.False(plan.RequiresElevation);
    }

    [Fact]
    public void Create_Install_UsesInstallPrePostCommands()
    {
        var options = new InstallOptions
        {
            PreInstallCommand = "pre.cmd",
            PostInstallCommand = "post.cmd",
            AbortOnPreInstallFail = true,
            PreUpdateCommand = "wrong-pre.cmd",
            PreUninstallCommand = "also-wrong.cmd",
        };

        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), options);

        Assert.Equal("pre.cmd", plan.PreCommand);
        Assert.Equal("post.cmd", plan.PostCommand);
        Assert.True(plan.AbortOnPreFail);
    }

    [Fact]
    public void Create_Upgrade_UsesUpdatePrePostCommands()
    {
        var options = new InstallOptions
        {
            PreUpdateCommand = "pre.cmd",
            PostUpdateCommand = "post.cmd",
            AbortOnPreUpdateFail = true,
        };

        var plan = OperationRequestFactory.Create(OperationKind.Upgrade, Row, DefaultSettings(), options);

        Assert.Equal("pre.cmd", plan.PreCommand);
        Assert.Equal("post.cmd", plan.PostCommand);
        Assert.True(plan.AbortOnPreFail);
    }

    [Fact]
    public void Create_Uninstall_UsesUninstallPrePostCommands()
    {
        var options = new InstallOptions
        {
            PreUninstallCommand = "pre.cmd",
            PostUninstallCommand = "post.cmd",
            AbortOnPreUninstallFail = true,
        };

        var plan = OperationRequestFactory.Create(OperationKind.Uninstall, Row, DefaultSettings(), options);

        Assert.Equal("pre.cmd", plan.PreCommand);
        Assert.Equal("post.cmd", plan.PostCommand);
        Assert.True(plan.AbortOnPreFail);
    }

    [Fact]
    public void Create_Install_Defaults_CommandsAreEmpty()
    {
        var plan = OperationRequestFactory.Create(OperationKind.Install, Row, DefaultSettings(), new InstallOptions());

        Assert.Equal("", plan.PreCommand);
        Assert.Equal("", plan.PostCommand);
        Assert.False(plan.AbortOnPreFail);
    }

    private static readonly PackageRow InstalledCli = new("GitHub CLI", "GitHub.cli", "2.98.0", "2.101.0", "winget");

    [Fact]
    public void CreateForVersion_OlderThanInstalled_IsAForcedInstallLabeledDowngrade()
    {
        string[] expected =
        [
            "install", "--id", "GitHub.cli", "--exact",
            "--source", "winget",
            "--version", "2.97.0",
            "--force",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        var plan = OperationRequestFactory.CreateForVersion(InstalledCli, "2.97.0", DefaultSettings(), new InstallOptions(), isInstalled: true);

        Assert.NotNull(plan);
        Assert.Equal(OperationKind.Install, plan.Kind);
        Assert.Equal(OperationPlan.DowngradeLabel, plan.Label);
        Assert.Equal(expected, WingetArguments.Install(plan.Request));
    }

    [Fact]
    public void CreateForVersion_NewerThanInstalled_IsAnUpgradeToThatVersion()
    {
        string[] expected =
        [
            "upgrade", "--id", "GitHub.cli", "--exact",
            "--source", "winget",
            "--version", "2.99.0",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        var plan = OperationRequestFactory.CreateForVersion(InstalledCli, "2.99.0", DefaultSettings(), new InstallOptions(), isInstalled: true);

        Assert.NotNull(plan);
        Assert.Equal(OperationKind.Upgrade, plan.Kind);
        Assert.Equal("upgrade", plan.Label);
        Assert.Equal(expected, WingetArguments.Upgrade(plan.Request));
    }

    [Fact]
    public void CreateForVersion_NotInstalled_IsAnInstallOfThatVersion()
    {
        string[] expected =
        [
            "install", "--id", "GitHub.cli", "--exact",
            "--source", "winget",
            "--version", "2.97.0",
            "--disable-interactivity", "--accept-source-agreements", "--accept-package-agreements",
        ];

        var plan = OperationRequestFactory.CreateForVersion(InstalledCli, "2.97.0", DefaultSettings(), new InstallOptions(), isInstalled: false);

        Assert.NotNull(plan);
        Assert.Equal(OperationKind.Install, plan.Kind);
        Assert.Equal("install", plan.Label);
        Assert.Equal(expected, WingetArguments.Install(plan.Request));
    }

    [Fact]
    public void CreateForVersion_InstalledVersion_ReturnsNull()
    {
        var plan = OperationRequestFactory.CreateForVersion(InstalledCli, "2.98.0", DefaultSettings(), new InstallOptions(), isInstalled: true);

        Assert.Null(plan);
    }

    [Fact]
    public void CreateForVersion_FewerSegmentsThanInstalled_CountsAsOlder()
    {
        var git = new PackageRow("Git", "Git.Git", "2.55.0.3", null, "winget");

        var plan = OperationRequestFactory.CreateForVersion(git, "2.55.0", DefaultSettings(), new InstallOptions(), isInstalled: true);

        Assert.NotNull(plan);
        Assert.Equal(OperationPlan.DowngradeLabel, plan.Label);
        Assert.True(plan.Request.Force);
    }

    [Fact]
    public void CreateForVersion_OlderVersion_KeepsTheInstallOptions()
    {
        var options = new InstallOptions { InstallationScope = "machine", PreInstallCommand = "pre.cmd" };

        var plan = OperationRequestFactory.CreateForVersion(InstalledCli, "2.97.0", DefaultSettings(), options, isInstalled: true);

        Assert.NotNull(plan);
        Assert.Equal("machine", plan.Request.Scope);
        Assert.True(plan.RequiresElevation);
        Assert.Equal("pre.cmd", plan.PreCommand);
    }

    [Theory]
    [InlineData(OperationKind.Install, "install")]
    [InlineData(OperationKind.Upgrade, "upgrade")]
    [InlineData(OperationKind.Uninstall, "uninstall")]
    public void Create_LabelNamesTheKind(OperationKind kind, string expected)
    {
        var plan = OperationRequestFactory.Create(kind, Row, DefaultSettings(), new InstallOptions());

        Assert.Equal(expected, plan.Label);
    }
}
