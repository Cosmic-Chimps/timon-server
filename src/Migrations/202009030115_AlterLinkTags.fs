namespace Links.Migrations

open FluentMigrator

[<Migration(202009030115L, "AlterLinkTags")>]
type AlterLinkTags() =
    inherit Migration()

    override this.Up() =
        this.Create.Column("IsCustom")
                  .OnTable("LinkTags")
                  .AsBoolean()
                  .WithDefaultValue(false) |> ignore
        ()

    override this.Down() =
        this.Delete.Column("IsCustom").FromTable("LinkTags") |> ignore
        ()

