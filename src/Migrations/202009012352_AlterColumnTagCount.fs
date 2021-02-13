namespace Links.Migrations

open FluentMigrator

[<Migration(202009012352L, "AlterColumnTagCount")>]
type AlterColumnTagCount() =
    inherit Migration()

    override this.Up() =
        this.Delete.Column("Count").FromTable("Tags") |> ignore

        this.Create.Column("Count")
                  .OnTable("Tags")
                  .AsInt64()
                  .WithDefaultValue(0) |> ignore

    override this.Down() =
        this.Delete.Column("Count").FromTable("Tags") |> ignore

        this.Create.Column("Count")
                  .OnTable("Tags")
                  .AsString()
                  .WithDefaultValue(0) |> ignore