open System
open FluentMigrator.Runner
open FluentMigrator.Runner.Initialization
open Microsoft.Extensions.Configuration
open TimonMigrations.Migrations
open Microsoft.Extensions.DependencyInjection
open Npgsql

let ensureDbExists (connectionString: string) =
    let connectionString' =
        connectionString.Replace("Database=timon", "Database=postgres")

    use conn = new NpgsqlConnection(connectionString')

    use command =
        new NpgsqlCommand(
            $"SELECT DATNAME FROM pg_catalog.pg_database WHERE DATNAME = 'timon'",
            conn
        )

    conn.Open()
    |> ignore

    let i = command.ExecuteScalar()

    if
        i <> null
        && i.ToString().Equals("timon")
    then
        conn.Close()
        ()
    else
        use commandCreateDb =
            new NpgsqlCommand(
                $"CREATE DATABASE \"timon\" WITH OWNER = postgres ENCODING = 'UTF8' CONNECTION LIMIT = -1;",
                conn
            )

        commandCreateDb.ExecuteNonQuery()
        |> ignore

        conn.Close()

        use conn2 = new NpgsqlConnection(connectionString)

        use commandCreateUidExtension =
            new NpgsqlCommand(
                $"CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\"",
                conn2
            )

        conn2.Open()
        |> ignore

        commandCreateUidExtension.ExecuteNonQuery()
        |> ignore

        conn2.Close()
        |> ignore

        ()


let configureRunner (rb: IMigrationRunnerBuilder) =
    let env =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")

    let builder =
        ConfigurationBuilder()
            .AddJsonFile($"appsettings.json", false, true)
            .AddJsonFile($"appsettings.{env}.json", true, true)
            .AddEnvironmentVariables()

    let config = builder.Build()

    let connectionString =
        match config.["CONNECTION_STRING"] with
        | null -> config.["TimonDatabase"]
        | _ -> config.["CONNECTION_STRING"]

    printfn "%s" config.["CONNECTION_STRING"]
    printfn "%s" connectionString
    printfn "%s" config.["TimonDatabase"]
    ensureDbExists (connectionString)

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
