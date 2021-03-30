open System
open FluentMigrator.Runner
open FluentMigrator.Runner.Initialization
open Microsoft.Extensions.Configuration
open TimonMigrations.Migrations
open Microsoft.Extensions.DependencyInjection

let env =
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")

let builder =
    ConfigurationBuilder()
        .AddJsonFile($"appsettings.json", true, true)
        .AddJsonFile($"appsettings.{env}.json", true, true)
        .AddEnvironmentVariables()

let config = builder.Build()

let configureRunner (rb: IMigrationRunnerBuilder) =
    // Configuration.GetValue<string>("CONNECTION_STRING")
    let connectionString =
        match config.["CONNECTION_STRING"] with
        | null -> config.["TimonDatabase"]
        | _ -> config.["CONNECTION_STRING"]

    rb
        .AddPostgres()
        .WithGlobalConnectionString(connectionString)
        .ScanIn(
            typeof<InitialMigration>
                .Assembly
        )
        .For.Migrations()
    |> ignore

let createServices () =
    ServiceCollection()
        .AddFluentMigratorCore()
        .ConfigureRunner(Action<IMigrationRunnerBuilder> configureRunner)
        .AddLogging(fun lb ->
            lb.AddFluentMigratorConsole()
            |> ignore)
        .BuildServiceProvider(false)

let updateDatabase (sp: IServiceProvider) =
    let runner =
        sp.GetRequiredService<IMigrationRunner>()

    runner.MigrateUp()

[<EntryPoint>]
let main argv =
    let serviceProvider = createServices ()

    use scope = serviceProvider.CreateScope()

    updateDatabase (scope.ServiceProvider)
    0 // return an integer exit code
