module LinkRepository

open DbProvider
open FSharp.Data.Sql
open System

let getClubLinkTags clubId linkId (dbCtx: DbProvider.Sql.dataContext) =
    query {
        for clubLinkTag in dbCtx.Public.ClubLinkTags do
            where (
                clubLinkTag.ClubId = clubId
                && clubLinkTag.LinkId = linkId
            )

            select clubLinkTag.TagName
    }

let getLink (dbCtx: Sql.dataContext) id =
    async {
        return!
            query {
                for t in dbCtx.Public.Links do
                    where (t.Id = id)
            }
            |> Seq.tryHeadAsync
    }
