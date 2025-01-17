using Recyclarr.Cli.Console.Commands;
using Spectre.Console.Cli;

namespace Recyclarr.Cli.Console;

public static class CliSetup
{
    public static void Commands(IConfigurator cli)
    {
        cli.AddCommand<SyncCommand>("sync")
            .WithExample("sync", "radarr", "--instance", "movies")
            .WithExample("sync", "-i", "instance1", "-i", "instance2")
            .WithExample("sync", "sonarr", "--preview");

        cli.AddCommand<MigrateCommand>("migrate");

        cli.AddBranch("list", list =>
        {
            list.SetDescription("List information from the guide");
            list.AddCommand<ListCustomFormatsCommand>("custom-formats");
            list.AddCommand<ListReleaseProfilesCommand>("release-profiles");
            list.AddCommand<ListQualitiesCommand>("qualities");
        });

        cli.AddBranch("config", config =>
        {
            config.SetDescription("Operations for configuration files");
            config.AddCommand<ConfigCreateCommand>("create");
            config.AddCommand<ConfigListCommand>("list");
        });

        cli.AddBranch("delete", delete =>
        {
            delete.SetDescription("Delete operations for remote services (e.g. Radarr, Sonarr)");
            delete.AddCommand<DeleteCustomFormatsCommand>("custom-formats");
        });
    }
}
