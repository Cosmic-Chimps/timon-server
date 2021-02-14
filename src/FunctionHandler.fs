module Function

open System
open FSharp.Data
open FluentMigrator.Runner
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.JwtBearer
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Handlers
open Links.Migrations
open Handlers.Helpers
open System.Threading.Tasks

let NotHandled (next: HttpFunc) (ctx: HttpContext) =
    setStatusCode HttpStatusCodes.MethodNotAllowed next ctx

let notLoggedIn =
    RequestErrors.UNAUTHORIZED "Basic" "Some Realm" "You must be logged in."

let mustBeAuthenticated = requiresAuthentication notLoggedIn

// let accessDenied = setStatusCode 401 >=> text "Access Denied"

let userMustBeInClub (clubId: Guid): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->

        match allowUserInClub ctx clubId with
        | true -> next ctx
        | false -> setStatusCode 401 earlyReturn ctx

let channelMustBeInClub (channelId: Guid) (clubId: Guid): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->

        match allowChannelInClub ctx channelId clubId with
        | true -> next ctx
        | false -> setStatusCode 401 earlyReturn ctx

let skip : HttpHandler =
    fun (next : HttpFunc) (_ : HttpContext) ->
        Task.FromResult None

let routes: HttpHandler =
    choose [
        // Public
        GET >=> route "/links" >=> LinkGet.Handle
        GET
        >=> routef "/links/tags/%s" (LinkGet.HandleSearchTag)
        subRoute
            "/.meta/v13"
            (choose [
                    GET >=> route "/get/channels" >=> ChannelGet.HandleMeta
                    GET >=> route "/get/links" >=> LinkGet.HandleMeta
                    GET >=> route "/get/clubs" >=> ClubHandler.HandleMeta
                    GET >=> route "/get/clubs/members" >=> ClubHandler.HandleMetaMembers
                    GET >=> route "/get/clubs/links" >=> LinkGet.HandleClubLinksMeta ])
        subRoute
           "/app"
            mustBeAuthenticated
            >=> choose [ POST >=> route "/users" >=> UserPost.Handle
                         POST >=> route "/clubs" >=> ClubHandler.Post
                         GET >=> route "/clubs" >=> ClubHandler.Get
                         GET
                         >=> route "/clubs/others"
                         >=> ClubHandler.GetOthers
                         POST
                         >=> route "/clubs/subscribe"
                         >=> ClubHandler.Subscribe
                         POST
                         >=> route "/clubs/unsubscribe"
                         >=> ClubHandler.UnSubscribe
                         subRoutef "/clubs/%O" (fun clubId ->
                             userMustBeInClub clubId
                             >=> choose [
                                          // Private
                                          GET
                                          >=> route "/channels"
                                          >=> ChannelGet.Handle clubId
                                          GET
                                          >=> route "/members"
                                          >=> ClubHandler.GetMembers clubId
                                          POST
                                          >=> route "/channels"
                                          >=> ChannelPost.Handle clubId
                                          subRoutef "/channels/%O" (fun channelId ->
                                              channelMustBeInClub channelId clubId
                                              >=> choose [ POST
                                                           >=> route "/links"
                                                           >=> LinkPost.Handle clubId channelId
                                                           GET
                                                           >=> route "/links"
                                                           >=> LinkGet.HandleByChannel clubId channelId ])
                                          GET
                                          >=> routef "/links/tags/%s" (LinkGet.HandleSearchTagByClub clubId)
                                          GET
                                          >=> routef "/links/search/%s" (LinkGet.HandleSearchByClub clubId)
                                          POST
                                          >=> routef "/links/%O/tags" (LinkPost.HandleTag clubId)
                                          DELETE
                                          >=> routef "/links/%O/tags/%s" (LinkPost.HandleDeleteTagFromLink clubId) ]) ]
        skip
   ]

let mutable Configuration = null

let configureAppConfiguration (_: WebHostBuilderContext) (config: IConfigurationBuilder) =
    let environment =
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")

    let environment' =
        if String.IsNullOrEmpty(environment) then "Production" else environment

    let configurationBuilder =
        config.AddJsonFile("appsettings.json", false, true)
              .AddJsonFile(sprintf "appsettings.%s.json" environment', true).AddEnvironmentVariables()

    Configuration <- configurationBuilder.Build()

let configureApp (app: IApplicationBuilder) =
    let errorHandler (ex: Exception) (logger: ILogger) =
        logger.LogError(EventId(), ex, "An unhandled exception has occurred while executing the request.")
        clearResponse
        >=> setStatusCode 500
        >=> text ex.Message

    // Add Giraffe to the ASP.NET Core pipeline
    app.UseAuthentication().UseGiraffeErrorHandler(errorHandler).UseGiraffe routes

    app.UseRouting()
       .UseCloudEvents()
       .UseEndpoints(fun (endpoints) ->
        endpoints.MapSubscribeHandler() |> ignore
        endpoints.MapControllers() |> ignore) |> ignore

    DbProvider.Sql.GetDataContext().``Design Time Commands``.SaveContextSchema
    |> ignore

let configureServices (services: IServiceCollection) =
#if DEBUG
    FSharp.Data.Sql.Common.QueryEvents.SqlQueryEvent
    |> Event.add (printfn "Executing SQL: %O")
#endif

    let authenticationOptions (o: AuthenticationOptions) =
        o.DefaultAuthenticateScheme <- JwtBearerDefaults.AuthenticationScheme
        o.DefaultChallengeScheme <- JwtBearerDefaults.AuthenticationScheme

    let jwtBearerOptions (cfg: JwtBearerOptions) =
        let identityAuthority = Configuration.["IdentityAuthority"]

        cfg.SaveToken <- true
        cfg.IncludeErrorDetails <- true
        cfg.Authority <- identityAuthority
        cfg.Audience <- "timon"

    services.AddGiraffe().AddAuthentication(authenticationOptions)
            .AddJwtBearer(Action<JwtBearerOptions> jwtBearerOptions)
    |> ignore

    services.AddControllers().AddDapr() |> ignore

    let configureRunner (rb: IMigrationRunnerBuilder) =
        rb.AddPostgres()
          // Set the connection string
          .WithGlobalConnectionString(Configuration.["TimonDatabase"])
          // Define the assembly containing the migrations
          .ScanIn(typeof<InitialMigration>.Assembly).For.Migrations()
        |> ignore

    services.AddFluentMigratorCore().ConfigureRunner(Action<IMigrationRunnerBuilder> configureRunner)
    |> ignore


    let serviceProvider = services.BuildServiceProvider()

    let migrationRunner =
        serviceProvider.GetService(typeof<IMigrationRunner>) :?> IMigrationRunner

    migrationRunner.MigrateUp()


// #if DEBUG
[<EntryPoint>]
let main _ =
    Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(fun webHostBuilder ->
        webHostBuilder.ConfigureAppConfiguration(configureAppConfiguration).Configure(configureApp)
                      .ConfigureServices(configureServices)
        |> ignore).Build().Run()
    0
// #endif
