module Handlers.Helpers

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Giraffe
open System.Linq
open System

let getDbCtx (ctx: HttpContext) =
    ctx.GetService<IConfiguration>()
    |> (fun s -> s.["TimonDatabase"])
    |> DbProvider.Sql.GetDataContext

let getUserId (ctx: HttpContext) =
    let userIdentity = ctx.User
    let timonUserClaim = userIdentity.Claims.FirstOrDefault(fun (x) -> x.Type = "timonUser")
    Guid.Parse(timonUserClaim.Value)

let allowUserInClub (ctx: HttpContext) (clubId: Guid) =
    let dbCtx = getDbCtx ctx
    let userId = getUserId ctx

    let exists =
        query {
            for clubUser in dbCtx.Public.ClubUsers do
            where (clubUser.ClubId = clubId && clubUser.UserId = userId)
            select 1
        } |> Seq.tryHead

    match exists with
    | Some _ -> true
    | None -> false

let allowChannelInClub (ctx: HttpContext) (channelId: Guid) (clubId: Guid) =
    match channelId with
    | test when test = Guid.Empty -> true
    | _ ->
        let dbCtx = getDbCtx ctx
        let userId = getUserId ctx

        let exists =
            query {
                for channel in dbCtx.Public.Channels do
                where (channel.Id = channelId && channel.ClubId = clubId)
                select 1
            } |> Seq.tryHead

        match exists with
        | Some _ -> true
        | None -> false
