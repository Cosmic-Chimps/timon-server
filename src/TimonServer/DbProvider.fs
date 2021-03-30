module DbProvider

open FSharp.Data.Sql

// #if LOCAL
[<Literal>]
let connectionString =
    "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=timon"
// #else
// let [<Literal>] connectionString = "Host=host.docker.internal;Port=5432;Username=postgres;Password=postgres;Database=timon"
// #endif

type Sql =
    SqlDataProvider<ConnectionString=connectionString, DatabaseVendor=Common.DatabaseProviderTypes.POSTGRESQL, IndividualsAmount=1000, UseOptionTypes=true>
// SqlDataProvider<ContextSchemaPath="timon_schema.txt", ConnectionString=connectionString, DatabaseVendor=Common.DatabaseProviderTypes.POSTGRESQL, IndividualsAmount=1000, UseOptionTypes=true>

type User = Sql.dataContext.``public.UsersEntity``
type Link = Sql.dataContext.``public.LinksEntity``
type Club = Sql.dataContext.``public.ClubsEntity``
type Channel = Sql.dataContext.``public.ChannelsEntity``
type ChannelLink = Sql.dataContext.``public.ChannelLinksEntity``
type ChannelFollowers = Sql.dataContext.``public.ChannelFollowersEntity``
type ChannelFollowings = Sql.dataContext.``public.ChannelFollowingsEntity``
type ChannelActivityPub = Sql.dataContext.``public.ChannelActivityPubEntity``

let saveDatabase (dbCtx: Sql.dataContext) =
    async {
        dbCtx.SubmitUpdatesAsync()
        |> Async.Catch
        |> Async.RunSynchronously
        |> (fun x ->
            match x with
            | Choice1Of2 _ -> printfn "Unit"
            | Choice2Of2 error ->
                printfn "Error! %s %O" error.Message error.Data)
    }
