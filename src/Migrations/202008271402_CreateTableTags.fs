namespace Links.Migrations

open FluentMigrator

[<Migration(202008271402L, "CreateTableTags")>]
type CreateTableTags() =
    inherit Migration()

    override this.Up () =
        this.Create.Table("Tags")
            .WithColumn("Name").AsString().NotNullable().PrimaryKey()
            .WithColumn("Count").AsString()
            .WithColumn("DateCreated").AsDateTimeOffset().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("LastUpdated").AsDateTimeOffset().WithDefault(SystemMethods.CurrentUTCDateTime) |> ignore

        this.Create.Table("LinkTags")
            .WithColumn("TagName").AsString().ForeignKey("Tags", "Name")
            .WithColumn("LinkId").AsGuid().ForeignKey("Links", "Id") |> ignore

        this.Create.PrimaryKey("LinkTags_Composite").OnTable("LinkTags").Columns("TagName", "LinkId") |> ignore

    override this.Down () =
        this.Delete.Table("LinkTags") |> ignore
        this.Delete.Table("Tags") |> ignore

