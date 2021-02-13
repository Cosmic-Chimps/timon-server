module Handlers.ClubHandler

open Giraffe
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open FSharp.Data
open FSharp.Data.Sql
open System


[<CLIMutable>]
type PostClubPayload = { name: string; isProtected: bool }

[<CLIMutable>]
type SubscribeClubPayload = { id: Guid; name: string }

[<CLIMutable>]
type UnSubscribeClubPayload = { id: Guid; name: string }

type MembersView = { displayName: string }

type ClubView =
    { id: Guid
      name: string
      isPublic: bool }


let createClub (dbCtx: DbProvider.Sql.dataContext) (payload: PostClubPayload) =
    async {
        let club = dbCtx.Public.Clubs.Create()
        club.Name <- payload.name
        club.DateCreated <- DateTime.UtcNow
        club.IsPublic <- not payload.isProtected
        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously
        return club
    }

let addUserToClub (dbCtx: DbProvider.Sql.dataContext) (userId: Guid) (clubId: Guid) =
    async {
        let! exists =
            query {
                for clubUser in dbCtx.Public.ClubUsers do
                    where
                        (clubUser.UserId = userId
                         && clubUser.ClubId = clubId)
                    select 1
            }
            |> Seq.tryHeadAsync

        return match exists with
               | Some _ -> ()
               | None ->
                   let clubUser = dbCtx.Public.ClubUsers.Create()
                   clubUser.ClubId <- clubId
                   clubUser.UserId <- userId
                   DbProvider.saveDatabase dbCtx
                   |> Async.RunSynchronously
                   ()
    }

let removeUserFromClub (dbCtx: DbProvider.Sql.dataContext) (userId: Guid) (clubId: Guid) =
    async {
        return query {
                   for clubUser in dbCtx.Public.ClubUsers do
                       where
                           (clubUser.UserId = userId
                            && clubUser.ClubId = clubId)
               }
               |> Seq.``delete all items from single table``
               |> Async.RunSynchronously
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

let Subscribe (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let! payload = ctx.BindJsonAsync<SubscribeClubPayload>()

        let userId = getUserId ctx

        addUserToClub dbCtx userId payload.id
        |> Async.RunSynchronously
        |> ignore

        return! setStatusCode HttpStatusCodes.OK next ctx
    }

let UnSubscribe (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let! payload = ctx.BindJsonAsync<UnSubscribeClubPayload>()

        let userId = getUserId ctx

        removeUserFromClub dbCtx userId payload.id
        |> Async.RunSynchronously
        |> ignore

        return! setStatusCode HttpStatusCodes.OK next ctx
    }


let Post (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let! payload = ctx.BindJsonAsync<PostClubPayload>()

        let userId = getUserId ctx

        let! club = createClub dbCtx payload

        createChannel dbCtx club.Id
        |> Async.RunSynchronously
        |> ignore

        addUserToClub dbCtx userId club.Id
        |> Async.RunSynchronously
        |> ignore

        return! setStatusCode HttpStatusCodes.Created next ctx
    }

let Get (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let userId = getUserId ctx

        let clubs =
            query {
                for clubUser in dbCtx.Public.ClubUsers do
                    for club in clubUser.``public.Clubs by Id`` do
                        where (clubUser.UserId = userId)
                        select
                            ({ id = club.Id
                               name = club.Name
                               isPublic = club.IsPublic })
            }
            |> Seq.toList

        return! json clubs next ctx
    }

let GetOthers (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let userId = getUserId ctx

        let userClubs =
            query {
                for clubUser in dbCtx.Public.ClubUsers do
                    where (clubUser.UserId = userId)
                    select (clubUser.ClubId)
            }

        let clubs =
            query {
                for club in dbCtx.Public.Clubs do
                    where (club.Id |<>| userClubs && club.IsPublic)
                    select
                        ({ id = club.Id
                           name = club.Name
                           isPublic = club.IsPublic })
            }
            |> Seq.toList

        return! json clubs next ctx
    }

let GetMembers clubId (next: HttpFunc) (ctx: HttpContext) =
    task {
        let dbCtx = getDbCtx ctx

        let userNames =
            query {
                for clubUser in dbCtx.Public.ClubUsers do
                    for user in clubUser.``public.Users by Id`` do
                        where (clubUser.ClubId = clubId)
                        select { displayName = user.DisplayName }
            }
            |> Seq.toList

        return! json userNames next ctx
    }

let HandleMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let result =
            [| { id = Guid.NewGuid()
                 name = "ClubName"
                 isPublic = true } |]

        return! json result next ctx
    }

let HandleMetaMembers (next: HttpFunc) (ctx: HttpContext) =
    task {
        let result = [| { displayName = "abc@xyz.com" } |]

        return! json result next ctx
    }
