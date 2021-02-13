module Handlers.LinkPost

open System
open System.Security.Claims
open FSharp.Data
open FSharp.Data.Sql.Runtime
open Giraffe
open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.V2.ContextInsensitive
open FSharp.Data.Sql
open OpenGraphNet
open Links.BackgroundActor
open Handlers.Helpers
open System.Net
open FSharp.Data.Sql

[<CLIMutable>]
type PostLinkPayload =
    { url: string
      via: string
      tagName: string }

[<CLIMutable>]
type PostTagPayload = { tags: string }

let findLinkById (dbCtx: DbProvider.Sql.dataContext) id =
    async {
        return! query {
                    for link in dbCtx.Public.Links do
                        where (link.Id = id)
                }
                |> Seq.headAsync
    }

let findLinkByUrlAsync (dbCtx: DbProvider.Sql.dataContext) url =
    async {
        return! query {
                    for link in dbCtx.Public.Links do
                        where (link.Url = url)
                }
                |> Seq.tryHeadAsync
    }

let findChannelById (dbCtx: DbProvider.Sql.dataContext) channelId clubId =
    async {
        return! query {
                    for channel in dbCtx.Public.Channels do
                        where (channel.Id = channelId && channel.ClubId = clubId)
                }
                |> Seq.tryHeadAsync

    }

let findChannelByName (dbCtx: DbProvider.Sql.dataContext) name clubId =
    async {
        return! query {
                    for channel in dbCtx.Public.Channels do
                        where (channel.Name = name && channel.ClubId = clubId)
                }
                |> Seq.tryHeadAsync
    }

let getMetaDataValue (m: Metadata.StructuredMetadata) = m.Value |> WebUtility.HtmlDecode

let getOrCreateLink (dbCtx: DbProvider.Sql.dataContext) (url: string) (tagName: string) =
    async {
        let! graph = OpenGraph.ParseUrlAsync(url) |> Async.AwaitTask

        let results = HtmlDocument.Parse(graph.OriginalHtml)

        let titleNode =
            results.Descendants [ "title" ] |> Seq.tryHead

        let rawUrl =
            (if isNull (box graph.Url) then graph.OriginalUrl else graph.Url)

        let uri = Uri(url)

        let host = uri.Host

        let! optionDbLink = findLinkByUrlAsync dbCtx (rawUrl.ToString())

        let dbLink =
            match optionDbLink with
            | Some dbLink' -> dbLink'
            | None _ -> dbCtx.Public.Links.Create()

        let tags =
            match graph.Metadata.TryGetValue "article:tag" with
            | true, value ->
                value
                |> Seq.map getMetaDataValue
                |> String.concat ","
            | _ -> String.Empty

        let tags' =
            match String.IsNullOrEmpty(tags) with
            | true -> tagName
            | false ->
                match String.IsNullOrEmpty(tagName) with
                | true -> tags
                | false -> sprintf "%s,%s" tags tagName

        let ogDescription =
            match graph.Metadata.TryGetValue "og:description" with
            | true, value ->
                value
                |> Seq.map getMetaDataValue
                |> String.concat ","
            | _ -> String.Empty

        dbLink.Url <- if isNull graph.Url then uri.AbsoluteUri.ToString() else graph.Url.ToString()

        dbLink.Title <-
            (if graph.Title = "" then
                match titleNode with
                | Some x -> x.InnerText()
                | None -> ""
             else
                 graph.Title)

        dbLink.ShortDescription <- ogDescription

        dbLink.Tags <- tags'

        dbLink.DomainName <- host

        DbProvider.saveDatabase dbCtx
        |> Async.RunSynchronously

        processor.Post(UpdateTags(dbCtx.CreateConnection().ConnectionString, tags', dbLink.Id))

        return dbLink
    }

// let getOrCreateChannel (dbCtx: DbProvider.Sql.dataContext) (channelIdStr: string) (clubId: Guid) =
//     async {

//         let optionDbChannel =
//             match channelIdStr with
//             | "" -> None
//             | _ ->
//                 let channelId = Guid.Parse(channelIdStr)
//                 findChannelById dbCtx channelId
//                 |> Async.RunSynchronously

//         match optionDbChannel with
//         | Some dbChannel -> return dbChannel
//         | None _ ->
//             let! optionDefaultChannel = findChannelByName dbCtx "general"

//             match optionDefaultChannel with
//             | Some defaultChannel -> return defaultChannel
//             | None _ ->
//                 let channel = dbCtx.Public.Channels.Create()
//                 channel.Name <- "general"
//                 DbProvider.saveDatabase dbCtx
//                 |> Async.RunSynchronously
//                 return channel
//     }

let getChannelId (dbCtx: DbProvider.Sql.dataContext) (channelId: Guid) (clubId: Guid) =
    async {
        let! optionDbChannel =
            async {
                match channelId with
                | test when test = Guid.Empty ->
                    let channelOption = findChannelByName dbCtx "general" clubId
                    return! channelOption
                | _ -> return None
            }


        match optionDbChannel with
        | Some dbChannel -> return dbChannel.Id
        | None _ -> return channelId
    }

let findUserById (dbCtx: DbProvider.Sql.dataContext) id =
    async {
        return! query {
                    for user in dbCtx.Public.Users do
                        where (user.Id = id)
                }
                |> Seq.tryHeadAsync

    }

let getOrCreateUser (dbCtx: DbProvider.Sql.dataContext) (uid: string) (email: string) =
    async {
        let uidGuid = Guid.Parse(uid)

        let! optionsUser = findUserById dbCtx uidGuid

        match optionsUser with
        | Some dbUser -> return dbUser
        | None _ ->
            let user = dbCtx.Public.Users.Create()
            user.Id <- uidGuid
            user.Email <- email
            user.DisplayName <- Array.head (email.Split("@"))
            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously
            return user
    }

let findChannelLinkByIds (dbCtx: DbProvider.Sql.dataContext) (linkId: Guid) (channelId: Guid) =
    async {
        let! channelLinkOption =
            query {
                for channelLink in dbCtx.Public.ChannelLinks do
                    where
                        (channelLink.ChannelId = channelId
                         && channelLink.LinkId = linkId)
            }
            |> Seq.tryHeadAsync

        match channelLinkOption with
        | Some channelLink ->
            channelLink.LastUpdated <- DateTime.UtcNow
            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously
        | None _ -> ()

        return channelLinkOption
    }

let Handle (clubId: Guid) (paramsChannelId: Guid): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let! payload = ctx.BindJsonAsync<PostLinkPayload>()

            let! link = getOrCreateLink dbCtx payload.url payload.tagName

            let userId = getUserId ctx

            let postSlackTo =
                { channel = "#general"
                  url = payload.url }

            processor.Post(PostSlack(postSlackTo))

            let! channelId = getChannelId dbCtx paramsChannelId clubId

            let! channelLinkOption = findChannelLinkByIds dbCtx link.Id channelId

            return! match channelLinkOption with
                    | Some _ -> setStatusCode HttpStatusCodes.OK next ctx
                    | None _ ->
                        let channelLink = dbCtx.Public.ChannelLinks.Create()
                        channelLink.ChannelId <- channelId
                        channelLink.LinkId <- link.Id
                        channelLink.Via <- payload.via
                        channelLink.UserId <- userId

                        DbProvider.saveDatabase dbCtx
                        |> Async.RunSynchronously

                        setStatusCode HttpStatusCodes.Created next ctx
        }

let HandleTag (clubId: Guid) (linkId: Guid): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let! payload = ctx.BindJsonAsync<PostTagPayload>()

            let sanitizedTags =
                payload.tags.Split(",")
                |> Seq.map (fun x -> x.Trim())
                |> Seq.filter (fun x -> not (String.IsNullOrEmpty(x)))
                |> String.concat ","

            return! match sanitizedTags with
                    | "" -> setStatusCode HttpStatusCodes.Created next ctx
                    | _ ->
                        let dbTags =
                            query {
                                for clubLinkTag in dbCtx.Public.ClubLinkTags do
                                    where
                                        (clubLinkTag.ClubId = clubId
                                         && clubLinkTag.LinkId = linkId)
                                    select clubLinkTag.TagName
                            }
                            |> String.concat ","
                        // findLinkById dbCtx linkId |> Async.RunSynchronously

                        let allTags = sprintf "%s,%s" dbTags sanitizedTags

                        processor.Post
                            (UpdateClubTags(dbCtx.CreateConnection().ConnectionString, allTags, clubId, linkId))

                        // DbProvider.saveDatabase dbCtx
                        // |> Async.RunSynchronously

                        setStatusCode HttpStatusCodes.Created next ctx
        }

let HandleDeleteTagFromLink (clubId: Guid) (linkId: Guid, tagName: string): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let dbTags =
                query {
                    for clubLinkTag in dbCtx.Public.ClubLinkTags do
                        where
                            (clubLinkTag.ClubId = clubId
                             && clubLinkTag.LinkId = linkId)
                        select clubLinkTag.TagName
                }
                |> String.concat ","

            let newTags =
                dbTags.Split(",")
                |> Array.filter (fun t -> t <> tagName)
                |> String.concat (",")

            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously

            processor.Post(UpdateClubTags(dbCtx.CreateConnection().ConnectionString, newTags, clubId, linkId))

            return! setStatusCode HttpStatusCodes.OK next ctx
        }
