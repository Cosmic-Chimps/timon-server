module Handlers.ChannelPost

open FSharp.Data
open FSharp.Data.Sql
open Giraffe
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open System

[<CLIMutable>]
type PostChannelPayload = {
    name: string
}

let findChannelByName (dbCtx: DbProvider.Sql.dataContext) name =
    async {
        return!
            query {
                for channel in dbCtx.Public.Channels do
                where (channel.Name = name)
            } |> Seq.tryHeadAsync
    }

let Handle (clubId: Guid) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
    task {
        let dbCtx = getDbCtx(ctx)

        let! payload = ctx.BindJsonAsync<PostChannelPayload>()

        return! findChannelByName dbCtx payload.name
                |> Async.RunSynchronously
                |> (fun x ->
                    match x with
                    | Some _ ->
                        setStatusCode HttpStatusCodes.OK next ctx
                    | None ->
                        let channel = dbCtx.Public.Channels.Create()
                        channel.Name <- payload.name
                        channel.ClubId <- clubId
                        DbProvider.saveDatabase dbCtx |> Async.RunSynchronously
                        setStatusCode HttpStatusCodes.OK next ctx
                    )

    }
