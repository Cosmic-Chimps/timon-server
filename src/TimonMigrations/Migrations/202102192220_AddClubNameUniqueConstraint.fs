namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202102192220L, "AddClubNameUniqueConstraint")>]
type AddClubNameUniqueConstraint() =
    inherit Migration()

    override this.Up() =
        this
            .Alter
            .Table("Clubs")
            .AlterColumn("Name")
            .AsString()
            .Unique()
        |> ignore

    override this.Down() =
        this
            .Alter
            .Table("Clubs")
            .AlterColumn("Name")
            .AsString()
        |> ignore

        ()
