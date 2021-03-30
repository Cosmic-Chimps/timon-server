module ClubRepository

open DbProvider
open FSharp.Data.Sql

let getClub (dbCtx: Sql.dataContext) id =
    async {
        return! query {
                    for t in dbCtx.Public.Clubs do
                        where (t.Id = id)
                }
                |> Seq.headAsync
    }
