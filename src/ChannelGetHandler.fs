module Handlers.ChannelGet

open System
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers

type Channel = {
    id: Guid
    name: string
}

let Handle (clubId: Guid)  =
    fun (next: HttpFunc) (ctx: HttpContext) ->
    task {
        let dbCtx = getDbCtx ctx

        let channelView =
            query {
                for channel in dbCtx.Public.Channels do
                where (channel.ClubId = clubId)
                select {
                    id = channel.Id
                    name = channel.Name
                }
            }

        return! json channelView next ctx
    }

let HandleMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let result = [|
            {
                id = Guid.NewGuid()
                name = "ChannelName"
            }
        |]

        return! json result next ctx
    }
