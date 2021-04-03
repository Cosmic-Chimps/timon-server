module Handlers.Helpers

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Giraffe
open System.Linq
open System
open DbProvider
open System.Text.RegularExpressions
open FSharp.Json
open System.Security.Cryptography
open Microsoft.AspNetCore.DataProtection
open Password
open System.Collections.Generic
open System.Text.Json
open ChannelRepository

let lift f x =
    match x with
    | Some v -> v |> f
    | None -> None

let getDbCtx (ctx: HttpContext) =
    ctx.GetService<IConfiguration>()
    |> (fun config ->
        match config.["CONNECTION_STRING"] with
        | null -> config.["TimonDatabase"]
        | _ -> config.["CONNECTION_STRING"])
    |> DbProvider.Sql.GetDataContext

let getUserId (ctx: HttpContext) =
    let userIdentity = ctx.User

    let timonUserClaim =
        userIdentity.Claims.FirstOrDefault(fun x -> x.Type = "timonUser")

    Guid.Parse(timonUserClaim.Value)

let isUserAllowedInClub (ctx: HttpContext) (clubId: Guid) =
    let dbCtx = getDbCtx ctx
    let userId = getUserId ctx

    let exists =
        query {
            for clubUser in dbCtx.Public.ClubUsers do
                where (
                    clubUser.ClubId = clubId
                    && clubUser.UserId = userId
                )

                select 1
        }
        |> Seq.tryHead

    match exists with
    | Some _ -> true
    | None -> false

let isChannelInClub (ctx: HttpContext) (channelId: Guid) (clubId: Guid) =
    match channelId with
    | test when test = Guid.Empty -> true
    | _ ->
        let dbCtx = getDbCtx ctx
        let userId = getUserId ctx

        let exists =
            query {
                for channel in dbCtx.Public.Channels do
                    where (
                        channel.Id = channelId
                        && channel.ClubId = clubId
                    )

                    select 1
            }
            |> Seq.tryHead

        match exists with
        | Some _ -> true
        | None -> false

[<CLIMutable>]
type RegisterActivityPubRequest =
    { Username: string
      Password: string
      VerifyPassword: string
      Email: string
      Redirect: string }

let encryptPassword (ctx: HttpContext) (value: string) =
    let internalTimonKeyValueProtection =
        ctx.GetService<IConfiguration>()
        |> (fun s -> s.["InternalTimonKeyValueProtection"])

    let protectionProvider =
        ctx.GetService<IDataProtectionProvider>()

    let protector =
        protectionProvider.CreateProtector(internalTimonKeyValueProtection)

    protector.Protect(value)

let decryptPassword (ctx: HttpContext) (value: string) =
    let internalTimonKeyValueProtection =
        ctx.GetService<IConfiguration>()
        |> (fun s -> s.["InternalTimonKeyValueProtection"])

    let protectionProvider =
        ctx.GetService<IDataProtectionProvider>()

    let protector =
        protectionProvider.CreateProtector(internalTimonKeyValueProtection)

    protector.Unprotect(value)

let generateSlug (value: string) =
    let temp =
        value
            .ToLower()
            .Substring(0, Math.Min(value.Length, 40))

    let temp1 = Regex.Replace(temp, @"[^a-z0-9\s-]", "")
    let temp2 = Regex.Replace(temp1, @"\s+", " ")
    Regex.Replace(temp2, @"\s", "-")


let registerChannelActivityPub
    (ctx: HttpContext)
    (club: Club)
    (channel: Channel)
    =
    let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

    let channelSlug = generateSlug channel.Name
    let clubSlug = generateSlug club.Name

    let activityPubDomain =
        ctx.GetService<IConfiguration>()
        |> (fun s -> s.["ActivityPubDomain"])

    let password = PasswordGenerator.genPass 10

    let username = $"{channelSlug}-{clubSlug}"

    let payload =
        { Username = username
          Password = password
          VerifyPassword = password
          Email = $"{channelSlug}-{clubSlug}@{activityPubDomain}"
          Redirect = String.Empty }

    let request =
        daprClient.CreateInvokeMethodRequest<RegisterActivityPubRequest>(
            "timon-activity-pub",
            "message-bus/register-channel-activitypub",
            payload
        )

    let registered =
        daprClient.InvokeMethodWithResponseAsync(request)
        |> Async.AwaitTask
        |> Async.RunSynchronously

    match registered.StatusCode with
    | System.Net.HttpStatusCode.Created ->
        let dbCtx = getDbCtx ctx

        let activityPubUserId = registered.Headers.Location

        let encryptedPassword = encryptPassword ctx password

        let channelActivityPub = dbCtx.Public.ChannelActivityPub.Create()
        channelActivityPub.ChannelId <- channel.Id
        channelActivityPub.Password <- encryptedPassword
        channelActivityPub.Username <- username
        channelActivityPub.ActivityPubId <- activityPubUserId.ToString()

        saveDatabase dbCtx
        |> Async.RunSynchronously

        ()
    | _ -> raise (Failure "Error registerChannelActivityPub")


[<CLIMutable>]
type LoginActivityPubRequest =
    { Username: string
      Password: string
      Redirect: string }

[<CLIMutable>]
type LoginActivityPubResponse = { Token: string }

let getChannelActivityPub (dbCtx: DbProvider.Sql.dataContext) channelId =
    query {
        for t in dbCtx.Public.ChannelActivityPub do
            where (t.ChannelId = channelId)
    }
    |> Seq.tryHead

let getChannelActivityPubToken (ctx: HttpContext) (channel: Channel) =
    let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

    let dbCtx = getDbCtx ctx

    let opChannelActivityPub = getChannelActivityPub dbCtx channel.Id

    match opChannelActivityPub with
    | None -> None
    | Some channelActivityPub ->
        let decryptedPassword =
            decryptPassword ctx channelActivityPub.Password

        let payload =
            { Username = channelActivityPub.Username
              Password = decryptedPassword
              Redirect = String.Empty }

        let request =
            daprClient.CreateInvokeMethodRequest<LoginActivityPubRequest>(
                "timon-activity-pub",
                "message-bus/login-channel-activitypub",
                payload
            )

        daprClient.InvokeMethodAsync<LoginActivityPubResponse>(request)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> function
        | x -> Some x

let proxyCall
    (ctx: HttpContext)
    (tokenResponse: LoginActivityPubResponse)
    (channel: Channel)
    (followUserId: string)
    =
    let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

    let requestProxy =
        daprClient.CreateInvokeMethodRequest("timon-activity-pub", "auth/proxy")

    // requestProxy.Headers.Add(
    //     "Content-Type",
    //     "application/x-www-form-urlencoded;charset=UTF-8"
    // )
    // |> ignore

    requestProxy.Headers.Add(
        "Accept",
        "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json"
    )
    |> ignore

    requestProxy.Headers.Add("Authorization", $"Bearer {tokenResponse.Token}")
    |> ignore

    let contentBody : KeyValuePair<string, string> array =
        [| new KeyValuePair<string, string>("id", followUserId) |]

    requestProxy.Content <-
        new System.Net.Http.FormUrlEncodedContent(contentBody)

    daprClient.InvokeMethodWithResponseAsync(requestProxy)
    |> Async.AwaitTask
    |> Async.RunSynchronously


[<CLIMutable>]
type ActivityPubRequest =
    { [<System.Text.Json.Serialization.JsonPropertyName("@context")>]
      Context: string array
      Type: string array
      Actor: string array
      Object: string array
      To: string array }

let followUser (ctx: HttpContext) (channel: Channel) (followUserId: string) =
    let dbCtx = getDbCtx ctx

    let opTokenResponse = getChannelActivityPubToken ctx channel

    match opTokenResponse with
    | None -> None
    | Some tokenResponse ->
        proxyCall ctx tokenResponse channel followUserId
        |> ignore

        let opChannelActivityPub = getChannelActivityPub dbCtx channel.Id

        match opChannelActivityPub with
        | None -> None
        | Some channelActivityPub ->
            let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

            let opTokenResponse2 = getChannelActivityPubToken ctx channel

            match opTokenResponse2 with
            | None -> None
            | Some tokenResponse2 ->
                let payload =
                    { Context = [| "https://www.w3.org/ns/activitystreams" |]
                      Type = [| "Follow" |]
                      Actor = [| channelActivityPub.ActivityPubId |]
                      Object = [| followUserId |]
                      To = [| followUserId |] }

                let requestProxy =
                    daprClient.CreateInvokeMethodRequest<ActivityPubRequest>(
                        "timon-activity-pub",
                        $"users/{channelActivityPub.Username}/outbox",
                        payload
                    )

                requestProxy.Headers.Add(
                    "Accept",
                    "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json"
                )
                |> ignore

                requestProxy.Headers.Add(
                    "Authorization",
                    $"Bearer {tokenResponse2.Token}"
                )
                |> ignore

                let resp =
                    daprClient.InvokeMethodWithResponseAsync(requestProxy)
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                Some resp.IsSuccessStatusCode


[<CLIMutable>]
type PostActivityPubOutbox =
    { [<System.Text.Json.Serialization.JsonPropertyName("@context")>]
      Context: string array
      Type: string array
      Name: string array
      Content: string array
      To: string array }


let postLinkToActivityPub (ctx: HttpContext) (link: Link) (channelId: Guid) =
    let dbCtx = getDbCtx ctx

    let channel =
        getChannel dbCtx channelId
        |> Async.RunSynchronously

    let opChannelActivityPub = getChannelActivityPub dbCtx channelId

    // let tokenResponse = getChannelActivityPubToken ctx channel

    // let channelActivityPub = getChannelActivityPub dbCtx channel.Id

    match opChannelActivityPub with
    | None -> None
    | Some channelActivityPub ->

        let opTokenResponse = getChannelActivityPubToken ctx channel

        match opTokenResponse with
        | None -> None
        | Some tokenResponse ->
            let daprClient = ctx.GetService<Dapr.Client.DaprClient>()

            let activityPubHost =
                channelActivityPub.ActivityPubId.IndexOf("users")
                |> fun x -> channelActivityPub.ActivityPubId.Substring(0, x - 1)

            let activityPubPayload =
                { Context =
                      [| "https://www.w3.org/ns/activitystreams"
                         $"{activityPubHost}/render/context" |]
                  Type = [| "Note" |]
                  Name = [| link.Url |]
                  Content = [| link.Url |]
                  To = [| $"{channelActivityPub.ActivityPubId}/followers" |] }

            let requestProxy =
                daprClient.CreateInvokeMethodRequest<PostActivityPubOutbox>(
                    "timon-activity-pub",
                    $"users/{channelActivityPub.Username}/outbox",
                    activityPubPayload
                )

            requestProxy.Headers.Add(
                "Accept",
                "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json"
            )
            |> ignore

            requestProxy.Headers.Add(
                "Authorization",
                $"Bearer {tokenResponse.Token}"
            )
            |> ignore

            let resp =
                daprClient.InvokeMethodWithResponseAsync(requestProxy)
                |> Async.AwaitTask
                |> Async.RunSynchronously

            Some resp.IsSuccessStatusCode
