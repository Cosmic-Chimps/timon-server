module Handlers.ChannelHandler

open System
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open FSharp.Data
open FSharp.Data.Sql
open Giraffe
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open System

type Channel =
    { Id: Guid
      Name: string
      ActivityPubId: string }

type ChannelFollow = { Name: string }

type ChannelActivityPubTo = { ActivityPubId: string }

let GetChannels (clubId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let channelView =
                query {
                    for channel in dbCtx.Public.Channels do
                        for channelActivityPub in (!!)
                                                      channel.``public.ChannelActivityPub by Id`` do
                            where (channel.ClubId = clubId)

                            select
                                { Id = channel.Id
                                  Name = channel.Name
                                  ActivityPubId =
                                      channelActivityPub.ActivityPubId }
                }

            return! json channelView next ctx
        }

let GetChannelsMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let result =
            [| { Id = Guid.NewGuid()
                 Name = "ChannelName"
                 ActivityPubId = "ActivityPubId" } |]

        return! json result next ctx
    }

[<CLIMutable>]
type CreateFollowerPayload = { ActivityPubId: string }

let PostFollow (clubId: Guid) (channelId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let! opChannel =
                ChannelRepository.getChannelOrDefault dbCtx clubId channelId

            match opChannel with
            | None -> return! setStatusCode HttpStatusCodes.BadRequest next ctx
            | Some channel ->
                let! payload = ctx.BindJsonAsync<CreateFollowerPayload>()

                match Helpers.followUser ctx channel (payload.ActivityPubId) with
                | Some true ->
                    let channelFollowing =
                        dbCtx.Public.ChannelFollowings.Create()

                    channelFollowing.ChannelId <- channelId
                    channelFollowing.ActivityPubId <- payload.ActivityPubId

                    DbProvider.saveDatabase dbCtx
                    |> Async.RunSynchronously

                    return! setStatusCode HttpStatusCodes.Created next ctx
                | Some false ->
                    return! setStatusCode HttpStatusCodes.BadRequest next ctx
                | None ->
                    return! setStatusCode HttpStatusCodes.BadRequest next ctx
        }

let GetFollowers (channelId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let followers =
                ChannelRepository.findChannelFollowers dbCtx channelId
                |> Async.RunSynchronously
                |> Seq.map (fun c -> { Name = c.ActivityPubId })

            return! json followers next ctx
        }

let GetFollowersMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let result =
            [| { Name = "https://timon/users/illya" } |]

        return! json result next ctx
    }

let GetFollowings (channelId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let followers =
                ChannelRepository.findChannelFollowings dbCtx channelId
                |> Async.RunSynchronously
                |> Seq.map (fun c -> { Name = c.ActivityPubId })

            return! json followers next ctx
        }

let GetFollowingsMeta =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let result =
                [| { Name = "https://timon/users/followings" } |]

            return! json result next ctx
        }


[<CLIMutable>]
type PostChannelPayload = { Name: string }

let CreateChannel (clubId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx (ctx)

            let! payload = ctx.BindJsonAsync<PostChannelPayload>()

            return!
                ChannelRepository.findChannelByNameInClub
                    dbCtx
                    payload.Name
                    clubId
                |> Async.RunSynchronously
                |> (fun x ->
                    match x with
                    | Some _ -> setStatusCode HttpStatusCodes.Conflict next ctx
                    | None ->
                        task {
                            let channel = dbCtx.Public.Channels.Create()
                            channel.Name <- payload.Name
                            channel.ClubId <- clubId

                            DbProvider.saveDatabase dbCtx
                            |> Async.RunSynchronously

                            let! club = ClubRepository.getClub dbCtx clubId

                            registerChannelActivityPub ctx club channel

                            return! setStatusCode HttpStatusCodes.OK next ctx
                        }

                    )

        }

let CreateActivityPubId (clubId: Guid) (channelId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx (ctx)

            let! channel =
                ChannelRepository.findChannelByChannelId dbCtx channelId

            let! club = ClubRepository.getClub dbCtx clubId

            registerChannelActivityPub ctx club channel

            return! setStatusCode HttpStatusCodes.OK next ctx
        }


let GetChannelActivityPubDetails (channelId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx (ctx)

            let! channelActivityPubOption =
                ChannelRepository.findChannelActivityPubDetails dbCtx channelId

            return!
                match channelActivityPubOption with
                | None -> json { ActivityPubId = "" } next ctx
                | Some it -> json { ActivityPubId = it.ActivityPubId } next ctx
        }

let GetChannelActivityPubDetailsMeta =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let result =
                [| { ActivityPubId = "https://timon/users/2" } |]

            return! json result next ctx
        }
