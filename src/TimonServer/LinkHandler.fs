module Handlers.LinkHandler

open System
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open System.Linq
open Dapper
open FSharp.Data.Sql
open FSharp.Data
open FSharp.Data.Sql.Runtime
open OpenGraphNet
open Links.BackgroundActor
open System.Net
open LinkRepository

type Link =
    { Id: Guid
      Title: string
      Url: string
      ShortDescription: string
      DomainName: string
      Tags: string
      DateCreated: DateTime }

type Channel = { Id: Guid; Name: string }

type ChannelLink =
    { DateCreated: DateTime
      UpVotes: int
      DownVotes: int
      Via: string
      Tags: string }

type User = { DisplayName: string }

type Tag = { Name: string }

type LinkView =
    { Link: Link
      Channel: Channel
      Data: ChannelLink
      User: User
      CustomTags: string }

type GeAnonymoustLinksResult =
    { Links: Link list
      Page: int
      ShowNext: bool }

type GetLinksResult =
    { Links: LinkView list
      Page: int
      ShowNext: bool }

let fillCustomTags
    (dbCtx: DbProvider.Sql.dataContext)
    (linksView: LinkView list)
    (clubId: Guid)
    =

    let linkIds =
        linksView
        |> Seq.map (fun x -> x.Link.Id)
        |> Seq.toList

    let clubLinkTags =
        query {
            for clubLinkTags in dbCtx.Public.ClubLinkTags do
                where (clubLinkTags.ClubId = clubId)
                select clubLinkTags
        }
        |> Seq.filter (fun x -> linkIds.Contains(x.LinkId))
        |> Seq.map
            (fun x ->
                {| linkId = x.LinkId
                   tagName = x.TagName |})
        |> Seq.toList

    linksView
    |> List.map
        (fun l ->
            let clubTags =
                clubLinkTags
                |> Seq.filter (fun x -> x.linkId = l.Link.Id)
                |> Seq.map (fun x -> x.tagName)
                |> String.concat ","

            { l with CustomTags = clubTags })


let getLinks
    (dbCtx: DbProvider.Sql.dataContext)
    (page: int)
    (takeValue: int)
    (clubId: Guid)
    (channelIdParam: Guid option)
    =

    let queryChannel =
        match channelIdParam with
        | Some opChannelId ->
            match opChannelId with
            | channelId when channelId = Guid.Empty ->
                <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) ->
                    channel.Id = channel.Id @>
            | channelId ->
                <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) ->
                    channel.Id = channelId @>
        | None ->
            <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) ->
                channel.Id = channel.Id @>

    let linkList =
        query {
            for club in dbCtx.Public.Clubs do
                for channel in club.``public.Channels by Id`` do
                    for channelLink in channel.``public.ChannelLinks by Id`` do
                        for user in channelLink.``public.Users by Id`` do
                            for link in channelLink.``public.Links by Id`` do
                                where (
                                    (%queryChannel) channel
                                    && club.Id = clubId
                                )

                                sortByDescending channelLink.DateCreated
                                skip (page * takeValue)
                                take takeValue

                                select (
                                    { Link =
                                          { Id = link.Id
                                            Title = link.Title
                                            Url = link.Url
                                            ShortDescription =
                                                link.ShortDescription
                                            DomainName = link.DomainName
                                            Tags = link.Tags
                                            DateCreated = link.DateCreated }
                                      Channel =
                                          { Id = channel.Id
                                            Name = channel.Name }
                                      Data =
                                          { DateCreated =
                                                channelLink.DateCreated
                                            UpVotes = channelLink.UpVotes
                                            DownVotes = channelLink.DownVotes
                                            Via = channelLink.Via
                                            Tags = "" }
                                      User = { DisplayName = user.DisplayName }
                                      CustomTags = "" }
                                )
        }
        |> Seq.toList

    fillCustomTags dbCtx linkList clubId


let getPageIndex (ctx: HttpContext) =
    match ctx.TryGetQueryStringValue "page" with
    | Some value ->
        match Int32.TryParse(value.Trim()) with
        | true, intValue -> intValue
        | false, _ -> 0
    | _ -> 0

let getLinksHandler (next: HttpFunc) (ctx: HttpContext) =
    task {
        let takeValue = 10
        let page = getPageIndex ctx
        let dbCtx = getDbCtx ctx

        let linksView =
            query {
                for link in dbCtx.Public.Links do
                    sortByDescending link.DateCreated
                    skip (page * takeValue)
                    take takeValue

                    select (
                        { Id = link.Id
                          Title = link.Title
                          Url = link.Url
                          ShortDescription = link.ShortDescription
                          DomainName = link.DomainName
                          Tags = link.Tags
                          DateCreated = link.DateCreated }
                    )
            }
            |> Seq.toList

        let result : GeAnonymoustLinksResult =
            { Links = linksView
              Page = page
              ShowNext =
                  (if linksView.Length = 0 then
                       false
                   else
                       (linksView.Length % takeValue) = 0) }

        return! json result next ctx
    }

let getLinksByChannelHandler (clubId: Guid) (channelId: Guid) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let takeValue = 10
            let page = getPageIndex ctx
            let dbCtx = getDbCtx ctx

            let linksView =
                getLinks dbCtx page takeValue clubId (Some channelId)

            return!
                json
                    { Links = linksView
                      Page = page
                      ShowNext =
                          (if linksView.Length = 0 then
                               false
                           else
                               (linksView.Length % takeValue) = 0) }
                    next
                    ctx
        }

let searchLinksByClubHandler (clubId: Guid) (term: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let takeValue = 10

            let page =
                match ctx.TryGetQueryStringValue "page" with
                | Some value ->
                    match Int32.TryParse(value.Trim()) with
                    | true, intValue -> intValue
                    | false, _ -> 0
                | _ -> 0

            let queryFts = """
            select l."Id"
            from "Links" l
            join "ChannelLinks" chl on l."Id" = chl."LinkId"
            join "Channels" ch on ch."Id" = chl."ChannelId"
            where to_tsvector(l."Title" || ' ' ||  l."Tags" || ' '|| l."ShortDescription" ||  ' ' || l."DomainName" || ' ' || chl."Via" ) @@ to_tsquery(@term)
            and ch."ClubId" = @clubId
            order by l."DateCreated" desc
            offset  @offsetValue
            limit @takeValue;
        """

            let inline (=>) a b = a, box b
            let offsetValue = (page * takeValue)

            let term' =
                term.Split(" ")
                |> String.concat ("&")

            let data =
                dict [
                    "term" => term'
                    "clubId" => clubId
                    "offsetValue"
                    => offsetValue
                    "takeValue"
                    => takeValue
                ]

            let dbCtx = getDbCtx ctx

            use conn = dbCtx.CreateConnection()

            // Execute the SQL query and get a reader
            let! linkIds =
                conn.QueryAsync<Guid>(queryFts, data)
                |> Async.AwaitTask

            let linkIds' = linkIds.ToArray()

            let linksView' =
                match linkIds'.Length > 0 with
                | true ->
                    let linksView =
                        query {
                            for channelLink in dbCtx.Public.ChannelLinks do
                                for link in channelLink.``public.Links by Id`` do
                                    for channel in channelLink.``public.Channels by Id`` do
                                        for user in channelLink.``public.Users by Id`` do
                                            where (linkIds'.Contains(link.Id))

                                            sortByDescending
                                                channelLink.DateCreated

                                            skip offsetValue
                                            take takeValue

                                            select (
                                                { Link =
                                                      { Id = link.Id
                                                        Title = link.Title
                                                        Url = link.Url
                                                        ShortDescription =
                                                            link.ShortDescription
                                                        DomainName =
                                                            link.DomainName
                                                        Tags = link.Tags
                                                        DateCreated =
                                                            link.DateCreated }
                                                  Channel =
                                                      { Id = channel.Id
                                                        Name = channel.Name }
                                                  Data =
                                                      { DateCreated =
                                                            channelLink.DateCreated
                                                        UpVotes =
                                                            channelLink.UpVotes
                                                        DownVotes =
                                                            channelLink.DownVotes
                                                        Via = channelLink.Via
                                                        Tags = "" }
                                                  User =
                                                      { DisplayName =
                                                            user.DisplayName }
                                                  CustomTags = "" }
                                            )
                        }
                        |> Seq.toList

                    linksView
                | false -> []

            return!
                json
                    { Links = linksView'
                      Page = page
                      ShowNext =
                          (if linksView'.Length = 0 then
                               false
                           else
                               (linksView'.Length % takeValue) = 0) }
                    next
                    ctx
        }

let searchLinksTagByClubHandler (clubId: Guid) (tagName: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let takeValue = 10
            let page = getPageIndex ctx

            let dbCtx = getDbCtx ctx

            let linksGeneralTagsView =
                query {
                    for clubLinkTags in dbCtx.Public.LinkTags do
                        for link in clubLinkTags.``public.Links by Id`` do
                            for tag in clubLinkTags.``public.Tags by Name`` do
                                for channelLink in link.``public.ChannelLinks by Id`` do
                                    for channel in channelLink.``public.Channels by Id`` do
                                        for user in channelLink.``public.Users by Id`` do
                                            where (
                                                tag.Name = tagName
                                                && channel.ClubId = clubId
                                            )

                                            sortByDescending
                                                channelLink.DateCreated

                                            skip (page * takeValue)
                                            take takeValue

                                            select (
                                                { Link =
                                                      { Id = link.Id
                                                        Title = link.Title
                                                        Url = link.Url
                                                        ShortDescription =
                                                            link.ShortDescription
                                                        DomainName =
                                                            link.DomainName
                                                        Tags = link.Tags
                                                        DateCreated =
                                                            link.DateCreated }
                                                  Channel =
                                                      { Id = channel.Id
                                                        Name = channel.Name }
                                                  Data =
                                                      { DateCreated =
                                                            channelLink.DateCreated
                                                        UpVotes =
                                                            channelLink.UpVotes
                                                        DownVotes =
                                                            channelLink.DownVotes
                                                        Via = channelLink.Via
                                                        Tags = "" }
                                                  User =
                                                      { DisplayName =
                                                            user.DisplayName }
                                                  CustomTags = "" }
                                            )
                }

            let linksClubTagsView =
                query {
                    for clubLinkTags in dbCtx.Public.ClubLinkTags do
                        for link in clubLinkTags.``public.Links by Id`` do
                            for tag in clubLinkTags.``public.Tags by Name`` do
                                for channelLink in link.``public.ChannelLinks by Id`` do
                                    for channel in channelLink.``public.Channels by Id`` do
                                        for user in channelLink.``public.Users by Id`` do
                                            where (
                                                tag.Name = tagName
                                                && channel.ClubId = clubId
                                            )

                                            sortByDescending
                                                channelLink.DateCreated

                                            skip (page * takeValue)
                                            take takeValue

                                            select (
                                                { Link =
                                                      { Id = link.Id
                                                        Title = link.Title
                                                        Url = link.Url
                                                        ShortDescription =
                                                            link.ShortDescription
                                                        DomainName =
                                                            link.DomainName
                                                        Tags = link.Tags
                                                        DateCreated =
                                                            link.DateCreated }
                                                  Channel =
                                                      { Id = channel.Id
                                                        Name = channel.Name }
                                                  Data =
                                                      { DateCreated =
                                                            channelLink.DateCreated
                                                        UpVotes =
                                                            channelLink.UpVotes
                                                        DownVotes =
                                                            channelLink.DownVotes
                                                        Via = channelLink.Via
                                                        Tags = "" }
                                                  User =
                                                      { DisplayName =
                                                            user.DisplayName }
                                                  CustomTags = "" }
                                            )
                }

            let linksView' =
                linksGeneralTagsView
                |> Seq.append linksClubTagsView
                |> Seq.toList

            let linksViewWithCustomTags = fillCustomTags dbCtx linksView' clubId

            return!
                json
                    { Links = linksViewWithCustomTags
                      Page = page
                      ShowNext =
                          (if linksViewWithCustomTags.Length = 0 then
                               false
                           else
                               (linksViewWithCustomTags.Length % takeValue) = 0) }
                    next
                    ctx
        }

let searchTagHandler (tagName: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let takeValue = 10
            let page = getPageIndex ctx

            let dbCtx = getDbCtx ctx

            let linksView =
                query {
                    for linkTag in dbCtx.Public.LinkTags do
                        for link in linkTag.``public.Links by Id`` do
                            for tag in linkTag.``public.Tags by Name`` do
                                for channelLink in link.``public.ChannelLinks by Id`` do
                                    for channel in channelLink.``public.Channels by Id`` do
                                        for user in channelLink.``public.Users by Id`` do
                                            where (tag.Name = tagName)

                                            sortByDescending
                                                channelLink.DateCreated

                                            skip (page * takeValue)
                                            take takeValue

                                            select (
                                                { Link =
                                                      { Id = link.Id
                                                        Title = link.Title
                                                        Url = link.Url
                                                        ShortDescription =
                                                            link.ShortDescription
                                                        DomainName =
                                                            link.DomainName
                                                        Tags = link.Tags
                                                        DateCreated =
                                                            link.DateCreated }
                                                  Channel =
                                                      { Id = channel.Id
                                                        Name = channel.Name }
                                                  Data =
                                                      { DateCreated =
                                                            channelLink.DateCreated
                                                        UpVotes =
                                                            channelLink.UpVotes
                                                        DownVotes =
                                                            channelLink.DownVotes
                                                        Via = channelLink.Via
                                                        Tags = "" }
                                                  User =
                                                      { DisplayName =
                                                            user.DisplayName }
                                                  CustomTags = "" }
                                            )
                }
                |> Seq.toList

            return!
                json
                    { Links = linksView
                      Page = page
                      ShowNext =
                          (if linksView.Length = 0 then
                               false
                           else
                               (linksView.Length % takeValue) = 0) }
                    next
                    ctx
        }

let HandleClubLinksMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let linksView =
            [ { Link =
                    { Id = Guid.NewGuid()
                      Title = "Title"
                      Url = "https://cosmic-chimps.com"
                      ShortDescription = "ShortDescription"
                      DomainName = "cosmic-chimps.com"
                      Tags = "hola,cosmic"
                      DateCreated = DateTime.UtcNow }
                Channel =
                    { Id = Guid.NewGuid()
                      Name = "ChannelName" }
                Data =
                    { DateCreated = DateTime.UtcNow
                      UpVotes = 0
                      DownVotes = 0
                      Via = "web"
                      Tags = "hola,cosmic" }
                User = { DisplayName = "chimp" }
                CustomTags = "cosmic,chimps" } ]

        return!
            json
                { Links = linksView
                  Page = 0
                  ShowNext = false }
                next
                ctx
    }


let HandleMeta (next: HttpFunc) (ctx: HttpContext) =
    task {
        let linksView =
            [ { Id = Guid.NewGuid()
                Title = "Title"
                Url = "https://cosmic-chimps.com"
                ShortDescription = "ShortDescription"
                DomainName = "cosmic-chimps.com"
                Tags = "hola,cosmic"
                DateCreated = DateTime.UtcNow } ]

        let result : GeAnonymoustLinksResult =
            { Links = linksView
              Page = 0
              ShowNext = false }

        return! json result next ctx
    }

[<CLIMutable>]
type PostLinkPayload =
    { url: string
      via: string
      tagName: string }

[<CLIMutable>]
type PostTagPayload = { tags: string }

let findLinkById (dbCtx: DbProvider.Sql.dataContext) id =
    async {
        return!
            query {
                for link in dbCtx.Public.Links do
                    where (link.Id = id)
            }
            |> Seq.headAsync
    }

let findLinkByUrlAsync (dbCtx: DbProvider.Sql.dataContext) url =
    async {
        return!
            query {
                for link in dbCtx.Public.Links do
                    where (link.Url = url)
            }
            |> Seq.tryHeadAsync
    }

let findChannelById (dbCtx: DbProvider.Sql.dataContext) channelId clubId =
    async {
        return!
            query {
                for channel in dbCtx.Public.Channels do
                    where (
                        channel.Id = channelId
                        && channel.ClubId = clubId
                    )
            }
            |> Seq.tryHeadAsync

    }


let getMetaDataValue (m: Metadata.StructuredMetadata) =
    m.Value
    |> WebUtility.HtmlDecode

let getOrCreateLink
    (dbCtx: DbProvider.Sql.dataContext)
    (url: string)
    (tagName: string)
    =
    async {
        let! graph =
            OpenGraph.ParseUrlAsync(url)
            |> Async.AwaitTask

        let results = HtmlDocument.Parse(graph.OriginalHtml)

        let titleNode =
            results.Descendants [ "title" ]
            |> Seq.tryHead

        let rawUrl =
            (if isNull (box graph.Url) then
                 graph.OriginalUrl
             else
                 graph.Url)

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

        dbLink.Url <-
            if isNull graph.Url then
                uri.AbsoluteUri.ToString()
            else
                graph.Url.ToString()

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

        processor.Post(UpdateTags(dbCtx, tags', dbLink.Id))

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

let getChannelId
    (dbCtx: DbProvider.Sql.dataContext)
    (channelId: Guid)
    (clubId: Guid)
    =
    async {
        let! optionDbChannel =
            async {
                match channelId with
                | test when test = Guid.Empty ->
                    let channelOption =
                        ChannelRepository.findChannelByName
                            dbCtx
                            "general"
                            clubId

                    return! channelOption
                | _ -> return None
            }


        match optionDbChannel with
        | Some dbChannel -> return dbChannel.Id
        | None _ -> return channelId
    }

let findUserById (dbCtx: DbProvider.Sql.dataContext) id =
    async {
        return!
            query {
                for user in dbCtx.Public.Users do
                    where (user.Id = id)
            }
            |> Seq.tryHeadAsync

    }

let getOrCreateUser
    (dbCtx: DbProvider.Sql.dataContext)
    (uid: string)
    (email: string)
    =
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

let findChannelLinkByIds
    (dbCtx: DbProvider.Sql.dataContext)
    (linkId: Guid)
    (channelId: Guid)
    =
    async {
        let! channelLinkOption =
            query {
                for channelLink in dbCtx.Public.ChannelLinks do
                    where (
                        channelLink.ChannelId = channelId
                        && channelLink.LinkId = linkId
                    )
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

let internalCreateLink
    (ctx: HttpContext)
    dbCtx
    payload
    userId
    channelId
    clubId
    =
    async {
        let! link = getOrCreateLink dbCtx payload.url payload.tagName

        let postSlackTo =
            { channel = "#general"
              url = payload.url }

        let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

        processor.Post(PostSlack(daprClient, postSlackTo))

        postLinkToActivityPub ctx link clubId channelId
        |> ignore

        let! channelId = getChannelId dbCtx channelId clubId

        let! channelLinkOption = findChannelLinkByIds dbCtx link.Id channelId

        match channelLinkOption with
        | Some _ -> ignore ()
        | None _ ->
            let channelLink = dbCtx.Public.ChannelLinks.Create()
            channelLink.ChannelId <- channelId
            channelLink.LinkId <- link.Id
            channelLink.Via <- payload.via
            channelLink.UserId <- userId

            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously
    }

let CreateLink (clubId: Guid) (paramsChannelId: Guid) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let userId = getUserId ctx

            let! payload = ctx.BindJsonAsync<PostLinkPayload>()

            internalCreateLink ctx dbCtx payload userId paramsChannelId clubId
            |> Async.RunSynchronously

            return! setStatusCode HttpStatusCodes.Created next ctx
        }

let HandleTag (clubId: Guid) (linkId: Guid) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let! payload = ctx.BindJsonAsync<PostTagPayload>()

            let sanitizedTags =
                payload.tags.Split(",")
                |> Seq.map (fun x -> x.Trim())
                |> Seq.filter (fun x -> not (String.IsNullOrEmpty(x)))
                |> String.concat ","

            return!
                match sanitizedTags with
                | "" -> setStatusCode HttpStatusCodes.Created next ctx
                | _ ->
                    let dbTags =
                        getClubLinkTags clubId linkId dbCtx
                        |> String.concat ","

                    let allTags = sprintf "%s,%s" dbTags sanitizedTags

                    processor.Post(
                        UpdateClubTags(dbCtx, allTags, clubId, linkId)
                    )

                    // DbProvider.saveDatabase dbCtx
                    // |> Async.RunSynchronously

                    setStatusCode HttpStatusCodes.Created next ctx
        }

let HandleDeleteTagFromLink
    (clubId: Guid)
    (linkId: Guid, tagName: string)
    : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let dbCtx = getDbCtx ctx

            let dbTags =
                getClubLinkTags clubId linkId dbCtx
                |> String.concat ","

            let newTags =
                dbTags.Split(",")
                |> Array.filter (fun t -> t <> tagName)
                |> String.concat (",")

            DbProvider.saveDatabase dbCtx
            |> Async.RunSynchronously

            processor.Post(UpdateClubTags(dbCtx, newTags, clubId, linkId))

            return! setStatusCode HttpStatusCodes.OK next ctx
        }
