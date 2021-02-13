module Handlers.UserPost

open System
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open FSharp.Data
open FSharp.Data.Sql

type CreateUserPayload =
    { id: Guid
      email: string
      displayName: string }

let createUser (dbCtx: DbProvider.Sql.dataContext) (payload: CreateUserPayload) =
    async {
        let user = dbCtx.Public.Users.Create()
        user.Id <- payload.id
        user.Email <- payload.email
        user.DisplayName <- Array.head (payload.email.Split("@"))
        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously
        return user
    }

let createClub (dbCtx: DbProvider.Sql.dataContext) (payload: CreateUserPayload) =
    async {
        let club = dbCtx.Public.Clubs.Create()
        club.Name <- payload.email
        club.DateCreated <- DateTime.UtcNow
        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously
        return club
    }

let createChannel (dbCtx: DbProvider.Sql.dataContext) (clubId: Guid) =
    async {
        let channel = dbCtx.Public.Channels.Create()
        channel.ClubId <- clubId
        channel.Name <- "general"
        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously
        return ()
    }

let addUserToClub (dbCtx: DbProvider.Sql.dataContext) (userId: Guid) (clubId: Guid) =
    async {
        let clubUser = dbCtx.Public.ClubUsers.Create()
        clubUser.ClubId <- clubId
        clubUser.UserId <- userId
        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously
        return ()
    }

let Handle (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let! payload = ctx.BindJsonAsync<CreateUserPayload>()

        let! user = createUser dbCtx payload

        let! club = createClub dbCtx payload

        createChannel dbCtx club.Id
        |> Async.RunSynchronously
        |> ignore

        addUserToClub dbCtx user.Id club.Id
        |> Async.RunSynchronously
        |> ignore

        return! json
                    {| clubId = club.Id
                       userId = user.Id
                       displayName = user.DisplayName |}
                    next
                    ctx

    // return! setStatusCode HttpStatusCodes.Created next ctx
    }
