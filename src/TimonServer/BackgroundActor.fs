module Links.BackgroundActor

open System
open FSharp.Data.Sql
open DbProvider
open System.Text
open FsHttp.DslCE
open FsHttp
open FSharp.Json
open ChannelRepository
open LinkRepository
open Microsoft.AspNetCore.Http
open Giraffe.Core
open Handlers.Helpers

type PostToSlack = { channel: string; url: string }
type PostLinkActivityPubTo = { channelLink: ChannelLink }

type private Tags = string
type private ClubId = Guid
type private LinkId = Guid

type BackgroundMessages =
    | UpdateClubTags of DbProvider.Sql.dataContext * Tags * ClubId * LinkId
    | UpdateTags of DbProvider.Sql.dataContext * string * Guid
    | PostSlack of Dapr.Client.DaprClient * PostToSlack

let updateTagByName (dbCtx: DbProvider.Sql.dataContext) (name: string) =
    async {
        let! tag =
            query {
                for tag in dbCtx.Public.Tags do
                    where (tag.Name = name)
            }
            |> Seq.tryHeadAsync

        return
            match tag with
            | Some t ->
                t.Count <- t.Count + 1L
                t
            | None ->
                let t = dbCtx.Public.Tags.Create()
                t.Name <- name
                t.Count <- 1L
                t.LastUpdated <- DateTime.UtcNow

                saveDatabase dbCtx
                |> Async.RunSynchronously

                t
    }

let updateLinkTagByName
    (dbCtx: DbProvider.Sql.dataContext)
    (name: string)
    (linkId: Guid)
    isCustom
    =
    async {
        updateTagByName dbCtx name
        |> Async.RunSynchronously
        |> ignore

        let linkTag = dbCtx.Public.LinkTags.Create()

        linkTag.TagName <- name

        linkTag.LinkId <- linkId

        linkTag.IsCustom <- isCustom

        saveDatabase dbCtx
        |> Async.RunSynchronously
    }

let refreshLinkTags
    (dbCtx: DbProvider.Sql.dataContext)
    (tags: string)
    (linkId: Guid)
    isCustom
    =
    async {

        query {
            for lt in dbCtx.Public.LinkTags do
                for l in lt.``public.Links by Id`` do
                    for t in lt.``public.Tags by Name`` do
                        where (
                            l.Id = linkId
                            && lt.IsCustom = false
                        )

                        select t
        }
        |> Seq.toArray
        |> Seq.iter (fun t -> t.Count <- t.Count - 1L)

        saveDatabase dbCtx
        |> Async.RunSynchronously

        query {
            for lt in dbCtx.Public.LinkTags do
                where (lt.LinkId = linkId)
        }
        |> Seq.``delete all items from single table``
        |> Async.RunSynchronously
        |> ignore

        saveDatabase dbCtx
        |> Async.RunSynchronously

        tags.Split(",")
        |> Seq.distinct
        |> Seq.map (fun x -> x.Trim())
        |> Seq.filter (fun x -> not (String.IsNullOrEmpty(x)))
        |> Seq.map (fun x -> updateLinkTagByName dbCtx x linkId isCustom)
        |> Async.Sequential
        |> Async.Ignore
        |> Async.RunSynchronously
        |> ignore

        saveDatabase dbCtx
        |> Async.RunSynchronously
    }

let updateClubTagByName
    (dbCtx: DbProvider.Sql.dataContext)
    (name: string)
    (clubId: Guid)
    (linkId: Guid)
    isCustom
    =
    async {
        updateTagByName dbCtx name
        |> Async.RunSynchronously
        |> ignore

        let! clubLinkTag =
            query {
                for clubLinkTag in dbCtx.Public.ClubLinkTags do
                    where (
                        clubLinkTag.ClubId = clubId
                        && clubLinkTag.LinkId = linkId
                        && clubLinkTag.TagName = name
                    )

                    select clubLinkTag
            }
            |> Seq.tryHeadAsync

        match clubLinkTag with
        | Some t ->
            t.Count <- t.Count + 1L

            saveDatabase dbCtx
            |> Async.RunSynchronously

            ()
        | None ->
            let t = dbCtx.Public.ClubLinkTags.Create()
            t.ClubId <- clubId
            t.Count <- 1L
            t.LinkId <- linkId
            t.TagName <- name

            saveDatabase dbCtx
            |> Async.RunSynchronously

            ()
    }

let refreshClubLinkTags
    (dbCtx: DbProvider.Sql.dataContext)
    (tags: string)
    (clubId: Guid)
    (linkId: Guid)
    isCustom
    =
    async {

        query {
            for clubLinkTag in dbCtx.Public.ClubLinkTags do
                for tag in clubLinkTag.``public.Tags by Name`` do
                    where (
                        clubLinkTag.ClubId = clubId
                        && clubLinkTag.LinkId = linkId
                    )

                    select tag
        }
        |> Seq.toArray
        |> Seq.iter (fun t -> t.Count <- t.Count - 1L)

        saveDatabase dbCtx
        |> Async.RunSynchronously

        query {
            for clubLinkTag in dbCtx.Public.ClubLinkTags do
                where (
                    clubLinkTag.ClubId = clubId
                    && clubLinkTag.LinkId = linkId
                )

                select clubLinkTag
        }
        |> Seq.toArray
        |> Seq.iter (fun t -> t.Count <- t.Count - 1L)

        saveDatabase dbCtx
        |> Async.RunSynchronously

        // query {
        //     for lt in dbCtx.Public.LinkTags do
        //         where (lt.LinkId = linkId)
        // }
        // |> Seq.``delete all items from single table``
        // |> Async.RunSynchronously
        // |> ignore

        // saveDatabase dbCtx |> Async.RunSynchronously

        tags.Split(",")
        |> Seq.distinct
        |> Seq.map (fun x -> x.Trim())
        |> Seq.filter (fun x -> not (String.IsNullOrEmpty(x)))
        |> Seq.map (fun x -> updateClubTagByName dbCtx x clubId linkId isCustom)
        |> Async.Sequential
        |> Async.Ignore
        |> Async.RunSynchronously
        |> ignore


        query {
            for clubLinkTag in dbCtx.Public.ClubLinkTags do
                where (
                    clubLinkTag.ClubId = clubId
                    && clubLinkTag.LinkId = linkId
                    && clubLinkTag.Count = 0L
                )

                select clubLinkTag
        }
        |> Seq.``delete all items from single table``
        |> Async.RunSynchronously
        |> ignore

        saveDatabase dbCtx
        |> Async.RunSynchronously
    }


let processor =
    MailboxProcessor.Start
        (fun inbox ->
            let rec processMessage () =
                async {
                    let! msg = inbox.Receive()

                    let! _ =
                        match msg with
                        | UpdateTags (dbCtx, tags, linkId) ->
                            // let dbCtx = DbProvider.Sql.GetDataContext connString
                            refreshLinkTags dbCtx tags linkId false
                        | UpdateClubTags (dbCtx, tags, clubId, linkId) ->
                            // let dbCtx = DbProvider.Sql.GetDataContext connString
                            refreshClubLinkTags dbCtx tags clubId linkId true
                        | PostSlack (daprClient, payload) ->
                            let request =
                                daprClient.CreateInvokeMethodRequest<PostToSlack>(
                                    "timon-slack",
                                    "post-slack",
                                    payload
                                )

                            daprClient.InvokeMethodWithResponseAsync(request)
                            |> Async.AwaitTask
                            |> Async.RunSynchronously
                            |> ignore

                            async { () }

                    return! processMessage ()
                }

            processMessage ())
