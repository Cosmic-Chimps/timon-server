module ChannelRepository

open DbProvider
open FSharp.Data.Sql
open System

let getChannel (dbCtx: Sql.dataContext) id =
    async {
        return!
            query {
                for t in dbCtx.Public.Channels do
                    where (t.Id = id)
            }
            |> Seq.headAsync
    }


let findChannelFollowers (dbCtx: Sql.dataContext) channelId =
    async {
        return!
            query {
                for t in dbCtx.Public.ChannelFollowers do
                    where (t.ChannelId = channelId)
            }
            |> Seq.executeQueryAsync
    }

let findChannelByNameInClub
    (dbCtx: DbProvider.Sql.dataContext)
    name
    (clubId: Guid)
    =
    async {
        return!
            query {
                for channel in dbCtx.Public.Channels do
                    where (
                        channel.Name = name
                        && channel.ClubId = clubId
                    )
            }
            |> Seq.tryHeadAsync
    }

let findChannelFollowings (dbCtx: Sql.dataContext) channelId =
    async {
        return!
            query {
                for t in dbCtx.Public.ChannelFollowings do
                    where (t.ChannelId = channelId)
            }
            |> Seq.executeQueryAsync
    }

let findChannelActivityPubDetails (dbCtx: Sql.dataContext) channelId =
    async {
        return!
            query {
                for t in dbCtx.Public.ChannelActivityPub do
                    where (t.ChannelId = channelId)
            }
            |> Seq.tryHeadAsync
    }

let findChannelByChannelId
    (dbCtx: DbProvider.Sql.dataContext)
    (channelId: Guid)
    =
    async {
        return!
            query {
                for channel in dbCtx.Public.Channels do
                    where (channel.Id = channelId)
            }
            |> Seq.headAsync
    }
