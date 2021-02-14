namespace TimonServer.Controllers

open System
open System.Collections.Generic
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging
open Dapr

[<CLIMutable>]
type AddNoteToTimonMessage =
  { content: string
    activityPubChannelId: string }


[<ApiController>]
[<Route("pubsub")>]
type PubSubController (logger : ILogger<PubSubController>) =
    inherit ControllerBase()

    [<HttpPost("add")>]
    [<Topic("messagebus", "add-note-to-timon")>]
    member this.Post(message: AddNoteToTimonMessage) : IActionResult =
      printf "%s" message.content
      printf "%s" message.activityPubChannelId
      this.Ok() :> IActionResult
