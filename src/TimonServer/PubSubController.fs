namespace TimonServer.Controllers

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Dapr
open System.Text.RegularExpressions
open Microsoft.Extensions.Configuration
open Handlers.Helpers
open Handlers.LinkHandler
open FSharp.Data.Sql
open FSharp.Data
open FSharp.Data.Sql.Runtime
open ChannelRepository
open Handlers.Helpers
open DbProvider

[<CLIMutable>]
type AddNoteToTimonMessage =
    { Content: string
      ActivityPubChannelId: string }

[<CLIMutable>]
type UserFollowChannelMessage =
    { FollowerId: string
      ActivityPubChannelId: string }

[<ApiController>]
[<Route("pubsub")>]
type PubSubController
    (
        logger: ILogger<PubSubController>,
        daprClient: Dapr.Client.DaprClient
    ) =
    inherit ControllerBase()

    let createChannelFollower
        (dbCtx: DbProvider.Sql.dataContext)
        (message: UserFollowChannelMessage)
        (cp: ChannelActivityPub)
        =
        let opChannelFollower =
            findChannelFollowerByActivityPubIdAndFollowerId
                dbCtx
                cp.ChannelId
                message.FollowerId
            |> Async.RunSynchronously

        match opChannelFollower with
        | Some _ -> None //this.BadRequest() :> IActionResult
        | None ->
            let channelFollower = dbCtx.Public.ChannelFollowers.Create()

            channelFollower.ChannelId <- cp.ChannelId
            channelFollower.ActivityPubId <- message.FollowerId

            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously

            Some true // this.Ok() :> IActionResult


    [<HttpPost("add")>]
    [<Topic("messagebus", "add-note-to-timon")>]
    member this.Post(message: AddNoteToTimonMessage) : IActionResult =

        Regex.Match(
            message.Content,
            @"http(s)?://([\w-]+\.)+[\w-]+(/[\w- ./?%&=]*)?"
        )
        |> fun m ->
            let dbCtx = getDbCtx this.HttpContext

            let payload : PostLinkPayload =
                { url = m.Value
                  via = message.ActivityPubChannelId
                  tagName = "" }

            let userId =
                query {
                    for user in dbCtx.Public.Users do
                        where (user.Email = "timon-bot@h-a-i.net")
                        select user.Id
                }
                |> Seq.head

            query {
                for channelFollowing in dbCtx.Public.ChannelFollowings do
                    where (
                        channelFollowing.ActivityPubId =
                            message.ActivityPubChannelId
                    )

                    select channelFollowing.ChannelId
            }
            |> Seq.toArray
            |> Seq.iter
                (fun channelId ->
                    internalCreateLink
                        this.HttpContext
                        dbCtx
                        payload
                        userId
                        channelId
                        Guid.Empty
                    |> Async.RunSynchronously)

        this.Ok() :> IActionResult

    [<Topic("messagebus", "user-follow-channel")>]
    member this.UserFollowChannel
        (message: UserFollowChannelMessage)
        : Async<IActionResult> =
        async {
            let dbCtx = getDbCtx this.HttpContext

            return
                findChannelByActivityPubId dbCtx message.ActivityPubChannelId
                |> Async.RunSynchronously
                |> lift (createChannelFollower dbCtx message)
                |> fun res ->
                    match res with
                    | None -> this.BadRequest() :> IActionResult
                    | Some _ -> this.Ok() :> IActionResult
        }
