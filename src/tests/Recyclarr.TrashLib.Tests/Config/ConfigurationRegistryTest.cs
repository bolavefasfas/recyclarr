using Recyclarr.TrashLib.Config;
using Recyclarr.TrashLib.Config.Parsing.ErrorHandling;
using Recyclarr.TrashLib.Config.Services;
using Recyclarr.TrashLib.ExceptionTypes;
using Recyclarr.TrashLib.TestLibrary;

namespace Recyclarr.TrashLib.Tests.Config;

[TestFixture]
[Parallelizable(ParallelScope.All)]
public class ConfigurationRegistryTest : TrashLibIntegrationFixture
{
    [Test]
    public void Use_explicit_paths_instead_of_default()
    {
        var sut = Resolve<ConfigurationRegistry>();

        Fs.AddFile("manual.yml", new MockFileData(
            """
            radarr:
              instance1:
                base_url: http://localhost:7878
                api_key: asdf
            """));

        var result = sut.FindAndLoadConfigs(new ConfigFilterCriteria
        {
            ManualConfigFiles = new[] {"manual.yml"}
        });

        result.Should().BeEquivalentTo(new[]
        {
            new RadarrConfiguration
            {
                BaseUrl = new Uri("http://localhost:7878"),
                ApiKey = "asdf",
                InstanceName = "instance1"
            }
        });
    }

    [Test]
    public void Throw_on_invalid_config_files()
    {
        var sut = Resolve<ConfigurationRegistry>();

        var act = () => sut.FindAndLoadConfigs(new ConfigFilterCriteria
        {
            ManualConfigFiles = new[] {"manual.yml"}
        });

        act.Should().ThrowExactly<InvalidConfigurationFilesException>();
    }

    [Test]
    public void Throw_on_invalid_instances()
    {
        var sut = Resolve<ConfigurationRegistry>();

        Fs.AddFile("manual.yml", new MockFileData(
            """
            radarr:
              instance1:
                base_url: http://localhost:7878
                api_key: asdf
            """));

        var act = () => sut.FindAndLoadConfigs(new ConfigFilterCriteria
        {
            ManualConfigFiles = new[] {"manual.yml"},
            Instances = new[] {"instance1", "instance2"}
        });

        act.Should().ThrowExactly<InvalidInstancesException>()
            .Which.InstanceNames.Should().BeEquivalentTo("instance2");
    }

    [Test]
    public void Throw_on_split_instances()
    {
        var sut = Resolve<ConfigurationRegistry>();

        Fs.AddFile("manual.yml", new MockFileData(
            """
            radarr:
              instance1:
                base_url: http://localhost:7878
                api_key: asdf
              instance2:
                base_url: http://localhost:7878
                api_key: asdf
            """));

        var act = () => sut.FindAndLoadConfigs(new ConfigFilterCriteria
        {
            ManualConfigFiles = new[] {"manual.yml"}
        });

        act.Should().ThrowExactly<SplitInstancesException>()
            .Which.InstanceNames.Should().BeEquivalentTo("instance1", "instance2");
    }
}
