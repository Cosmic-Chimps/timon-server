module Handlers.LinkGet

open System
open Microsoft.AspNetCore.Http
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Handlers.Helpers
open System.Linq
open Npgsql
open Dapper
open Microsoft.FSharp.Quotations
open FSharp.Data.Sql

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
    { links: Link list
      page: int
      showNext: bool }

type GetLinksResult =
    { links: LinkView list
      page: int
      showNext: bool }

let fillCustomTags (dbCtx: DbProvider.Sql.dataContext) (linksView: LinkView list) (clubId: Guid) =

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
        |> Seq.map (fun x ->
            {| linkId = x.LinkId
               tagName = x.TagName |})
        |> Seq.toList

    linksView
    |> List.map (fun l ->
        let clubTags =
            clubLinkTags
            |> Seq.filter (fun x -> x.linkId = l.Link.Id)
            |> Seq.map (fun x -> x.tagName)
            |> String.concat ","

        { l with CustomTags = clubTags })


let getLinks (dbCtx: DbProvider.Sql.dataContext)
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
                <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) -> channel.Id = channel.Id @>
            | channelId ->
                <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) -> channel.Id = channelId @>
        | None -> <@ fun (channel: DbProvider.Sql.dataContext.``public.ChannelsEntity``) -> channel.Id = channel.Id @>

    let linkList =
        query {
            for club in dbCtx.Public.Clubs do
                for channel in club.``public.Channels by Id`` do
                    for channelLink in channel.``public.ChannelLinks by Id`` do
                        for user in channelLink.``public.Users by Id`` do
                            for link in channelLink.``public.Links by Id`` do
                                where ((%queryChannel) channel && club.Id = clubId)
                                sortByDescending channelLink.DateCreated
                                skip (page * takeValue)
                                take takeValue
                                select
                                    ({ Link =
                                           { Id = link.Id
                                             Title = link.Title
                                             Url = link.Url
                                             ShortDescription = link.ShortDescription
                                             DomainName = link.DomainName
                                             Tags = link.Tags
                                             DateCreated = link.DateCreated }
                                       Channel = { Id = channel.Id; Name = channel.Name }
                                       Data =
                                           { DateCreated = channelLink.DateCreated
                                             UpVotes = channelLink.UpVotes
                                             DownVotes = channelLink.DownVotes
                                             Via = channelLink.Via
                                             Tags = "" }
                                       User = { DisplayName = user.DisplayName }
                                       CustomTags = "" })
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

let Handle (next: HttpFunc) (ctx: HttpContext) =
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
                    select
                        ({ Id = link.Id
                           Title = link.Title
                           Url = link.Url
                           ShortDescription = link.ShortDescription
                           DomainName = link.DomainName
                           Tags = link.Tags
                           DateCreated = link.DateCreated })
            }
            |> Seq.toList

        let result: GeAnonymoustLinksResult =
            { links = linksView
              page = page
              showNext = (if linksView.Length = 0 then false else (linksView.Length % takeValue) = 0) }

        return! json result next ctx
    }

let HandleByChannel (clubId: Guid) (channelId: Guid): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let takeValue = 10
            let page = getPageIndex ctx
            let dbCtx = getDbCtx ctx

            let linksView =
                getLinks dbCtx page takeValue clubId (Some channelId)

            return! json
                        { links = linksView
                          page = page
                          showNext = (if linksView.Length = 0 then false else (linksView.Length % takeValue) = 0) }
                        next
                        ctx
        }

let HandleSearchByClub (clubId: Guid) (term: string): HttpHandler =
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

            let term' = term.Split(" ") |> String.concat ("&")

            let data =
                dict [ "term" => term'
                       "clubId" => clubId
                       "offsetValue" => offsetValue
                       "takeValue" => takeValue ]

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
                                            sortByDescending channelLink.DateCreated
                                            skip offsetValue
                                            take takeValue
                                            select
                                                ({ Link =
                                                       { Id = link.Id
                                                         Title = link.Title
                                                         Url = link.Url
                                                         ShortDescription = link.ShortDescription
                                                         DomainName = link.DomainName
                                                         Tags = link.Tags
                                                         DateCreated = link.DateCreated }
                                                   Channel = { Id = channel.Id; Name = channel.Name }
                                                   Data =
                                                       { DateCreated = channelLink.DateCreated
                                                         UpVotes = channelLink.UpVotes
                                                         DownVotes = channelLink.DownVotes
                                                         Via = channelLink.Via
                                                         Tags = "" }
                                                   User = { DisplayName = user.DisplayName }
                                                   CustomTags = "" })
                        }
                        |> Seq.toList

                    linksView
                | false -> []

            return! json
                        { links = linksView'
                          page = page
                          showNext = (if linksView'.Length = 0 then false else (linksView'.Length % takeValue) = 0) }
                        next
                        ctx
        }

let HandleSearchTagByClub (clubId: Guid) (tagName: string): HttpHandler =
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
                                            where (tag.Name = tagName && channel.ClubId = clubId)
                                            sortByDescending channelLink.DateCreated
                                            skip (page * takeValue)
                                            take takeValue
                                            select
                                                ({ Link =
                                                       { Id = link.Id
                                                         Title = link.Title
                                                         Url = link.Url
                                                         ShortDescription = link.ShortDescription
                                                         DomainName = link.DomainName
                                                         Tags = link.Tags
                                                         DateCreated = link.DateCreated }
                                                   Channel = { Id = channel.Id; Name = channel.Name }
                                                   Data =
                                                       { DateCreated = channelLink.DateCreated
                                                         UpVotes = channelLink.UpVotes
                                                         DownVotes = channelLink.DownVotes
                                                         Via = channelLink.Via
                                                         Tags = "" }
                                                   User = { DisplayName = user.DisplayName }
                                                   CustomTags = "" })
                }

            let linksClubTagsView =
                query {
                    for clubLinkTags in dbCtx.Public.ClubLinkTags do
                        for link in clubLinkTags.``public.Links by Id`` do
                            for tag in clubLinkTags.``public.Tags by Name`` do
                                for channelLink in link.``public.ChannelLinks by Id`` do
                                    for channel in channelLink.``public.Channels by Id`` do
                                        for user in channelLink.``public.Users by Id`` do
                                            where (tag.Name = tagName && channel.ClubId = clubId)
                                            sortByDescending channelLink.DateCreated
                                            skip (page * takeValue)
                                            take takeValue
                                            select
                                                ({ Link =
                                                       { Id = link.Id
                                                         Title = link.Title
                                                         Url = link.Url
                                                         ShortDescription = link.ShortDescription
                                                         DomainName = link.DomainName
                                                         Tags = link.Tags
                                                         DateCreated = link.DateCreated }
                                                   Channel = { Id = channel.Id; Name = channel.Name }
                                                   Data =
                                                       { DateCreated = channelLink.DateCreated
                                                         UpVotes = channelLink.UpVotes
                                                         DownVotes = channelLink.DownVotes
                                                         Via = channelLink.Via
                                                         Tags = "" }
                                                   User = { DisplayName = user.DisplayName }
                                                   CustomTags = "" })
                }

            let linksView' =
                linksGeneralTagsView
                |> Seq.append linksClubTagsView
                |> Seq.toList

            let linksViewWithCustomTags = fillCustomTags dbCtx linksView' clubId

            return! json
                        { links = linksViewWithCustomTags
                          page = page
                          showNext =
                              (if linksViewWithCustomTags.Length = 0
                               then false
                               else (linksViewWithCustomTags.Length % takeValue) = 0) }
                        next
                        ctx
        }

let HandleSearchTag (tagName: string): HttpHandler =
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
                                            sortByDescending channelLink.DateCreated
                                            skip (page * takeValue)
                                            take takeValue
                                            select
                                                ({ Link =
                                                       { Id = link.Id
                                                         Title = link.Title
                                                         Url = link.Url
                                                         ShortDescription = link.ShortDescription
                                                         DomainName = link.DomainName
                                                         Tags = link.Tags
                                                         DateCreated = link.DateCreated }
                                                   Channel = { Id = channel.Id; Name = channel.Name }
                                                   Data =
                                                       { DateCreated = channelLink.DateCreated
                                                         UpVotes = channelLink.UpVotes
                                                         DownVotes = channelLink.DownVotes
                                                         Via = channelLink.Via
                                                         Tags = "" }
                                                   User = { DisplayName = user.DisplayName }
                                                   CustomTags = "" })
                }
                |> Seq.toList

            return! json
                        { links = linksView
                          page = page
                          showNext = (if linksView.Length = 0 then false else (linksView.Length % takeValue) = 0) }
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

        return! json
                    { links = linksView
                      page = 0
                      showNext = false }
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

        let result: GeAnonymoustLinksResult =
            { links = linksView
              page = 0
              showNext = false }

        return! json result next ctx
    }
