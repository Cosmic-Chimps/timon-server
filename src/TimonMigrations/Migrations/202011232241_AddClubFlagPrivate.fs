namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202011232241L, "AddClubFlagPrivate")>]
type AddClubFlagPrivate() =
    inherit Migration()

    override this.Up() =
        this
            .Create
            .Column("IsPublic")
            .OnTable("Clubs")
            .AsBoolean()
            .WithDefaultValue(false)
        |> ignore

        ()

    override this.Down() =
        this
            .Delete
            .Column("IsPublic")
            .FromTable("Clubs")
        |> ignore

        ()
