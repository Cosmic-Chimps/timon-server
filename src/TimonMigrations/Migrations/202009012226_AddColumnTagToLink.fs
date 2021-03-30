namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202009012226L, "AddColumnTagToLink")>]
type AddColumnTagToLink() =
    inherit Migration()

    override this.Up() =
        this
            .Alter
            .Table("Links")
            .AddColumn("Tags")
            .AsString()
            .WithDefaultValue("")
        |> ignore

    override this.Down() =
        this
            .Delete
            .Column("Tags")
            .FromTable("Links")
        |> ignore
