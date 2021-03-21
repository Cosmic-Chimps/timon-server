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

[<CLIMutable>]
type AddNoteToTimonMessage =
    { Content: string
      ActivityPubChannelId: string }


[<ApiController>]
[<Route("pubsub")>]
type PubSubController
    (
        logger: ILogger<PubSubController>,
        daprClient: Dapr.Client.DaprClient
    ) =
    inherit ControllerBase()

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
                    internalCreateLink dbCtx payload userId channelId Guid.Empty
                    |> Async.RunSynchronously)

        this.Ok() :> IActionResult
