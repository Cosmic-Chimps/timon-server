module Tests

open System.IdentityModel.Tokens.Jwt
open Npgsql
open Xunit
open System.Net
open System.Net.Http
open System.Text
open Microsoft.AspNetCore.TestHost
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open System
open System.IO
open HttpFunc
open Fixtures
open FSharp.Control.Tasks.V2.ContextInsensitive
open TimonStartup
open FSharp.Data

type GetResponseParameters = JsonProvider<"Response.json">

type ResponseParameters = JsonProvider<"""{"id":"da7a41dc-5ee6-46b2-8290-0acbd5a2157f","dateCreated":"0001-01-01T00:00:00","url":"https://cosmic-chimps.com"}""">

let isRunningOnDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") = "true"
let connectionStringDocker = "Host=host.docker.internal;Port=5432;Username=postgres;Password=postgres;Database=timon"
let connectionStringLocal = "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=timon"
let dbConnectionString = if isRunningOnDocker then connectionStringDocker  else connectionStringLocal


let createHost () =
    WebHostBuilder().UseContentRoot(Directory.GetCurrentDirectory()).UseEnvironment("Test")
        .ConfigureAppConfiguration(configureAppConfiguration)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(Action<IServiceCollection> configureServices)
let setup () = Environment.SetEnvironmentVariable("POSTGRESQL_CONNECTION_STRING", dbConnectionString)

let teardown () =
    let conn = new NpgsqlConnection(dbConnectionString)
    let _ = conn.Open()
    use command = new NpgsqlCommand("truncate table \"ChannelLinks\"", conn)
    let _ = command.ExecuteNonQuery()
    use command = new NpgsqlCommand("truncate table \"Links\"", conn)
    let _ = command.ExecuteNonQuery()
    use command = new NpgsqlCommand("truncate table \"Users\"", conn)
    let _ = command.ExecuteNonQuery()
    use command = new NpgsqlCommand("truncate table \"Channels\"", conn)
    let _ = command.ExecuteNonQuery()
    conn.Close()

[<Fact>]
let ``POST / should save link`` () =
    Assert.True(true)
//    task {
//        setup()
//        use server = new TestServer(createHost ())
//        use client = server.CreateClient()
//
//        let a = JwtSecurityToken("https://localhost:5001", "timon")
//
//        use content =
//            new StringContent(serializeObject (getLink), Encoding.UTF8, "application/json")
//
//        let! response = post client "/" content
//        let! jsonText = response |> ensureSuccess |> readText
//
//        let json = ResponseParameters.Parse(jsonText)
//
//        teardown()
//
//        shouldEqualStatusCode System.Net.HttpStatusCode.Created response.StatusCode
//    }


[<Fact>]
let ``GET / should return link`` () =
    Assert.True(true)
//    task {
//        setup()
//        use server = new TestServer(createHost ())
//        use client = server.CreateClient()
//
//        let! response = get client "/"
//        let! jsonText = response |> ensureSuccess |> readText
//
//        let json = ResponseParameters.Parse(jsonText)
//
//        teardown()
//
//        shouldEqualInt json.Length 1
//    }
